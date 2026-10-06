using System.ClientModel;
using System.Globalization;
using System.Text.Json;
using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Reading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

// PDF file input and reasoning effort are marked "evaluation" (OPENAI001) in the OpenAI .NET SDK.
#pragma warning disable OPENAI001

namespace InvoiceProcessing.Infrastructure.Readers;

public sealed class OpenAiOptions
{
    /// <summary>API key; if empty, the environment variable OPENAI_API_KEY is used.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-5.4-mini";

    /// <summary>Optional reasoning effort for reasoning models (e.g. "low"); leave empty for other models.</summary>
    public string? ReasoningEffort { get; set; }

    public int MaxOutputTokens { get; set; } = 16000;
    public int TimeoutSeconds { get; set; } = 180;
}

/// <summary>
/// Fallback for PDF invoices without e-invoice data: the PDF is sent to the OpenAI API, which returns the data
/// in the fixed JSON format (Structured Outputs). Works for digital and scanned PDFs.
/// </summary>
public sealed class OpenAiPdfReader(IOptions<OpenAiOptions> options, ILogger<OpenAiPdfReader> logger) : IInvoiceReader
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private ChatClient? _client;

    public bool CanRead(InvoiceDocument document) => document.Extension == ".pdf";

    /// <summary>The model can misread a character, and a second read usually doesn't repeat it (see DECISIONS.md, ADR-010).</summary>
    public bool ReadAgainOnValidationFailure => true;

    public async Task<ExtractionResult> ReadAsync(InvoiceDocument document, CancellationToken ct)
    {
        var settings = options.Value;
        var apiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new TransientProcessingException("Kein OpenAI-API-Key konfiguriert (OpenAI:ApiKey oder OPENAI_API_KEY).");

        // One client for the lifetime of the service (it is thread-safe and reuses its HTTP connections).
        var client = _client ??= new ChatClient(settings.Model, new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) });

        ChatMessage[] messages =
        [
            new SystemChatMessage(InvoiceExtractionSchema.Instructions),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart("Lies diese Eingangsrechnung aus."),
                ChatMessageContentPart.CreateFilePart(BinaryData.FromBytes(document.Content), "application/pdf", document.FileName)),
        ];

        var chatOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "invoice", BinaryData.FromString(InvoiceExtractionSchema.Json), "Rechnungsdaten", jsonSchemaIsStrict: true),
            MaxOutputTokenCount = settings.MaxOutputTokens,
            StoredOutputEnabled = false,
        };
        if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort))
            chatOptions.ReasoningEffortLevel = new ChatReasoningEffortLevel(settings.ReasoningEffort);

        ChatCompletion completion;
        try
        {
            completion = (await client.CompleteChatAsync(messages, chatOptions, ct)).Value;
        }
        catch (ClientResultException ex) when (ex.Status is 400 or 413 or 422)
        {
            throw new InvoiceReadException($"OpenAI hat die Datei abgelehnt ({ex.Status}): {FirstLine(ex.Message)}", ex);
        }
        catch (ClientResultException ex)
        {
            throw new TransientProcessingException($"OpenAI-Fehler ({ex.Status}): {FirstLine(ex.Message)}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TransientProcessingException($"OpenAI nicht erreichbar: {ex.Message}", ex);
        }

        if (!string.IsNullOrEmpty(completion.Refusal))
            throw new InvoiceReadException($"OpenAI hat die Verarbeitung abgelehnt: {completion.Refusal}");
        if (completion.FinishReason == ChatFinishReason.Length)
            throw new InvoiceReadException("Antwort von OpenAI wurde abgeschnitten (OpenAI:MaxOutputTokens erhöhen).");

        var json = completion.Content[0].Text;
        logger.LogDebug("OpenAI-Antwort für {File}: {Json}", document.FileName, json);

        var extracted = JsonSerializer.Deserialize<ExtractedInvoice>(json, JsonOptions)
                        ?? throw new InvoiceReadException("Leere Antwort von OpenAI.");
        if (!extracted.IsInvoice)
            throw new InvoiceReadException("Das Dokument ist laut OpenAI keine Rechnung.");

        var usage = completion.Usage;
        return new ExtractionResult(
            extracted.ToInvoice(),
            "PDF via OpenAI",
            $"PDF ohne E-Rechnungsdaten → ausgelesen mit OpenAI ({completion.Model}, {usage.TotalTokenCount.ToString("N0", German)} Tokens)");
    }

    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();

    private sealed record ExtractedInvoice(
        bool IsInvoice,
        ExtractedParty Supplier,
        string InvoiceNumber,
        string InvoiceDate,
        string? DueDate,
        string Currency,
        string? OrderReference,
        decimal? AllowanceTotal,
        decimal? ChargeTotal,
        decimal NetTotal,
        decimal TaxTotal,
        decimal GrossTotal,
        List<ExtractedLine> Lines)
    {
        public Invoice ToInvoice() => new()
        {
            InvoiceNumber = InvoiceNumber.Trim(),
            InvoiceDate = ParseDate(InvoiceDate) ?? throw new InvoiceReadException($"Ungültiges Rechnungsdatum \"{InvoiceDate}\"."),
            DueDate = ParseDate(DueDate),
            Currency = Currency.Trim().ToUpperInvariant(),
            Supplier = new Party(Supplier.Name.Trim(), Supplier.VatId, Supplier.Street, Supplier.PostalCode, Supplier.City),
            OrderReference = OrderReference,
            AllowanceTotal = AllowanceTotal ?? 0m,
            ChargeTotal = ChargeTotal ?? 0m,
            NetTotal = NetTotal,
            TaxTotal = TaxTotal,
            GrossTotal = GrossTotal,
            Lines = Lines.Select(l => new InvoiceLine
            {
                LineNumber = l.LineNumber,
                ArticleNumber = l.ArticleNumber,
                Description = l.Description.Trim(),
                Quantity = l.Quantity,
                FreeQuantity = l.FreeQuantity,
                Unit = l.Unit,
                UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent,
                DiscountAmount = l.DiscountAmount,
                NetAmount = l.NetAmount,
                VatRate = l.VatRate,
            }).ToList(),
        };

        private static DateOnly? ParseDate(string? value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private sealed record ExtractedParty(string Name, string? VatId, string? Street, string? PostalCode, string? City);

    private sealed record ExtractedLine(
        int LineNumber,
        string? ArticleNumber,
        string Description,
        decimal Quantity,
        decimal FreeQuantity,
        string? Unit,
        decimal UnitPrice,
        decimal? DiscountPercent,
        decimal? DiscountAmount,
        decimal NetAmount,
        decimal? VatRate);
}
