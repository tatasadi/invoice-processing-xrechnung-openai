using InvoiceProcessing.Core.Model;

namespace InvoiceProcessing.Core.Reading;

/// <summary>
/// Reads one kind of invoice document. Readers are asked in registration order; the first one whose
/// <see cref="CanRead"/> returns true handles the file. New sources (e.g. another e-invoice format or
/// another AI provider) are added by registering another reader.
/// </summary>
public interface IInvoiceReader
{
    bool CanRead(InvoiceDocument document);

    Task<ExtractionResult> ReadAsync(InvoiceDocument document, CancellationToken ct);
}

/// <param name="Invoice">The extracted invoice.</param>
/// <param name="Format">Short source format for the database, e.g. "XRechnung UBL".</param>
/// <param name="Description">How the file was read, for the log.</param>
public sealed record ExtractionResult(Invoice Invoice, string Format, string Description);

/// <summary>The file cannot be read as an invoice. Retrying will not help; the file goes to the error folder.</summary>
public sealed class InvoiceReadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A temporary problem (database or OpenAI not reachable, missing configuration). The file stays in the inbox and is retried.</summary>
public sealed class TransientProcessingException(string message, Exception? inner = null) : Exception(message, inner);
