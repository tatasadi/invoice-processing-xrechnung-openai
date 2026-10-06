using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Core.Reading;
using Microsoft.Extensions.Options;

namespace InvoiceProcessing.Infrastructure.Import;

/// <summary>Stores originals as Archiv\yyyy\MM\&lt;hash&gt;_&lt;file name&gt;; the hash prefix keeps names unique.</summary>
public sealed class FileDocumentArchive(IOptions<StorageOptions> options) : IDocumentArchive
{
    public async Task<string> StoreAsync(InvoiceDocument document, Invoice invoice, CancellationToken ct)
    {
        var relativePath = Path.Combine(
            invoice.InvoiceDate.ToString("yyyy"),
            invoice.InvoiceDate.ToString("MM"),
            $"{document.Sha256[..12]}_{document.FileName}");

        var fullPath = GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, document.Content, ct);
        return relativePath;
    }

    public void Delete(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }

    public string GetFullPath(string relativePath) => Path.Combine(options.Value.ArchivePath, relativePath);
}
