using InvoiceProcessing.Core.Model;

namespace InvoiceProcessing.Core.Processing;

/// <summary>Write side used by the import.</summary>
public interface IInvoiceRepository
{
    Task<ExistingInvoice?> FindByFileHashAsync(string sha256, CancellationToken ct);

    /// <summary>Same supplier + invoice number, or same invoice number + date + gross amount (if the supplier key differs between sources).</summary>
    Task<ExistingInvoice?> FindDuplicateAsync(Invoice invoice, InvoiceKeys keys, CancellationToken ct);

    /// <summary>Stores header and lines in one transaction. Throws <see cref="DuplicateInvoiceException"/> on a unique-key conflict.</summary>
    Task<long> InsertAsync(Invoice invoice, InvoiceSource source, CancellationToken ct);

    Task LogAsync(ProcessingLogEntry entry, CancellationToken ct);
}

/// <summary>Read side used by the user interface.</summary>
public interface IInvoiceQueries
{
    Task<IReadOnlyList<StoredInvoice>> ListInvoicesAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<InvoiceLine>> ListLinesAsync(long invoiceId, CancellationToken ct);

    Task<IReadOnlyList<ProcessingLogRow>> ListLogAsync(int limit, CancellationToken ct);
}

public sealed record ExistingInvoice(long Id, string InvoiceNumber, string SupplierName, string SourceFile);

public sealed record InvoiceKeys(string SupplierKey, string InvoiceNumberKey);

public sealed record InvoiceSource(string FileName, string Sha256, string Format, InvoiceKeys Keys, string ArchivePath);

public sealed record ProcessingLogEntry(
    string FileName,
    string Sha256,
    ProcessingStatus Status,
    string? Format,
    long? InvoiceId,
    string Message,
    IReadOnlyList<string> Errors,
    int DurationMs);

public sealed class DuplicateInvoiceException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class StoredInvoice
{
    public long Id { get; set; }
    public string SupplierName { get; set; } = "";
    public string? SupplierVatId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Currency { get; set; } = "";
    public decimal NetTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public long LineCount { get; set; }
    public string SourceFormat { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string? ArchivePath { get; set; }
    public DateTime ImportedAt { get; set; }
}

public sealed class ProcessingLogRow
{
    public long Id { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? SourceFormat { get; set; }
    public long? InvoiceId { get; set; }
    public string Message { get; set; } = "";
    public string? Errors { get; set; }
    public int DurationMs { get; set; }
}
