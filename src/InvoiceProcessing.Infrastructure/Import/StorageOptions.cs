namespace InvoiceProcessing.Infrastructure.Import;

public sealed class StorageOptions
{
    /// <summary>Defined inbox folder; processed with the button "Eingangsordner verarbeiten".</summary>
    public string InboxPath { get; set; } = @"C:\Rechnungsverarbeitung\Eingang";

    /// <summary>Originals of all imported invoices, sorted by invoice date (yyyy\MM).</summary>
    public string ArchivePath { get; set; } = @"C:\Rechnungsverarbeitung\Archiv";

    /// <summary>Files from the inbox that could not be imported, each with a .fehler.txt.</summary>
    public string FailedPath { get; set; } = @"C:\Rechnungsverarbeitung\Fehler";

    /// <summary>Files from the inbox that were already imported.</summary>
    public string DuplicatesPath { get; set; } = @"C:\Rechnungsverarbeitung\Duplikate";

    public void ResolveRelativeTo(string basePath)
    {
        InboxPath = Path.GetFullPath(InboxPath, basePath);
        ArchivePath = Path.GetFullPath(ArchivePath, basePath);
        FailedPath = Path.GetFullPath(FailedPath, basePath);
        DuplicatesPath = Path.GetFullPath(DuplicatesPath, basePath);
    }
}
