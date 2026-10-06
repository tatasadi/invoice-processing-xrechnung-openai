using System.Diagnostics;
using System.Globalization;
using InvoiceProcessing.Core.Reading;
using InvoiceProcessing.Core.Validation;
using Microsoft.Extensions.Logging;

namespace InvoiceProcessing.Core.Processing;

/// <summary>
/// Processes one document: duplicate check by file hash → read (e-invoice directly, otherwise PDF via AI)
/// → validate → duplicate check by supplier + invoice number → archive the original and store.
/// Every outcome is written to the processing log.
/// </summary>
public sealed class InvoiceProcessor(
    IEnumerable<IInvoiceReader> readers,
    InvoiceValidator validator,
    IInvoiceRepository repository,
    IDocumentArchive archive,
    ILogger<InvoiceProcessor> logger)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<ProcessingOutcome> ProcessAsync(InvoiceDocument document, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = await ProcessCoreAsync(document, ct) with { Duration = stopwatch.Elapsed };

        logger.Log(outcome.Status == ProcessingStatus.Imported ? LogLevel.Information : LogLevel.Warning,
            "{File}: {Status} – {Message}{Errors}", document.FileName, outcome.Status, outcome.Message,
            outcome.Errors.Count > 0 ? " | " + string.Join(" | ", outcome.Errors) : "");

        if (outcome.Status != ProcessingStatus.Retry)
        {
            var entry = new ProcessingLogEntry(document.FileName, document.Sha256, outcome.Status, outcome.Format,
                outcome.InvoiceId, outcome.Message, outcome.Errors, (int)outcome.Duration.TotalMilliseconds);
            try
            {
                await repository.LogAsync(entry, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Eintrag im Verarbeitungsprotokoll fehlgeschlagen");
            }
        }

        return outcome;
    }

    private async Task<ProcessingOutcome> ProcessCoreAsync(InvoiceDocument document, CancellationToken ct)
    {
        try
        {
            var sameFile = await repository.FindByFileHashAsync(document.Sha256, ct);
            if (sameFile is not null)
            {
                return new(ProcessingStatus.Duplicate,
                    $"Identische Datei wurde bereits importiert (Rechnung {sameFile.InvoiceNumber}, ID {sameFile.Id}).")
                {
                    InvoiceId = sameFile.Id,
                };
            }

            var reader = readers.FirstOrDefault(r => r.CanRead(document));
            if (reader is null)
                return new(ProcessingStatus.Failed, $"Dateiformat \"{document.Extension}\" wird nicht unterstützt (PDF oder XML).");

            ExtractionResult extraction;
            IReadOnlyList<string> errors;
            var secondRead = false;
            try
            {
                extraction = await ReadAsync(reader, document, ct);
                errors = validator.Validate(extraction.Invoice);

                // An AI read can get a single character wrong (e.g. 184,27 instead of 185,27). Read once more before
                // rejecting: a misread is usually not repeated, a wrongly printed invoice fails again.
                if (errors.Count > 0 && reader.ReadAgainOnValidationFailure)
                {
                    logger.LogInformation("{File}: Prüfung fehlgeschlagen ({Errors}) – zweites Lesen", document.FileName, string.Join(" | ", errors));
                    extraction = await ReadAsync(reader, document, ct);
                    errors = validator.Validate(extraction.Invoice);
                    secondRead = true;
                }
            }
            catch (InvoiceReadException ex)
            {
                return new(ProcessingStatus.Failed, $"Auslesen fehlgeschlagen: {ex.Message}");
            }

            var invoice = extraction.Invoice;
            var read = new ProcessingOutcome(ProcessingStatus.Imported, "")
            {
                Format = extraction.Format,
                Description = secondRead ? $"{extraction.Description} · zweimal gelesen" : extraction.Description,
                Invoice = invoice,
            };

            if (errors.Count > 0)
            {
                var message = secondRead
                    ? "Prüfung fehlgeschlagen, auch nach zweitem Lesen – nicht gespeichert."
                    : "Prüfung fehlgeschlagen – nicht gespeichert.";
                return read with { Status = ProcessingStatus.ValidationFailed, Message = message, Errors = errors };
            }

            var passed = secondRead ? "Prüfung beim zweiten Lesen bestanden" : "Prüfung bestanden";

            var keys = InvoiceKeyFactory.Create(invoice);
            var duplicate = await repository.FindDuplicateAsync(invoice, keys, ct);
            if (duplicate is not null)
            {
                return read with
                {
                    Status = ProcessingStatus.Duplicate,
                    Message = $"Rechnung {invoice.InvoiceNumber} von {duplicate.SupplierName} ist bereits importiert (ID {duplicate.Id}, aus {duplicate.SourceFile}).",
                    InvoiceId = duplicate.Id,
                };
            }

            var archivePath = await archive.StoreAsync(document, invoice, ct);
            try
            {
                var id = await repository.InsertAsync(invoice, new InvoiceSource(document.FileName, document.Sha256, extraction.Format, keys, archivePath), ct);
                return read with { Message = $"{passed} – gespeichert (ID {id}).", InvoiceId = id };
            }
            catch (Exception ex)
            {
                archive.Delete(archivePath);
                if (ex is DuplicateInvoiceException)
                    return read with { Status = ProcessingStatus.Duplicate, Message = ex.Message };
                throw;
            }
        }
        catch (TransientProcessingException ex)
        {
            return new(ProcessingStatus.Retry, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unerwarteter Fehler bei {File}", document.FileName);
            return new(ProcessingStatus.Failed, $"Unerwarteter Fehler: {ex.Message}");
        }
    }

    private async Task<ExtractionResult> ReadAsync(IInvoiceReader reader, InvoiceDocument document, CancellationToken ct)
    {
        var extraction = await reader.ReadAsync(document, ct);
        var invoice = extraction.Invoice;
        logger.LogInformation("{File}: {Description}; Rechnung {InvoiceNumber} von {Supplier}, {LineCount} Positionen, {Gross} {Currency} brutto",
            document.FileName, extraction.Description, invoice.InvoiceNumber, invoice.Supplier.Name, invoice.Lines.Count,
            invoice.GrossTotal.ToString("N2", German), invoice.Currency);
        return extraction;
    }
}
