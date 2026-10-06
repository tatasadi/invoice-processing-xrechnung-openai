using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Reading;

namespace InvoiceProcessing.Core.Processing;

/// <summary>Keeps the original file of every imported invoice; the database stores the path.</summary>
public interface IDocumentArchive
{
    /// <returns>Path of the stored file relative to the archive root.</returns>
    Task<string> StoreAsync(InvoiceDocument document, Invoice invoice, CancellationToken ct);

    void Delete(string relativePath);

    string GetFullPath(string relativePath);
}
