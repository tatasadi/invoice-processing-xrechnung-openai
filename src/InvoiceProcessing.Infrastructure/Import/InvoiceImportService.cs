using System.Text;
using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Core.Reading;
using Microsoft.Extensions.Options;

namespace InvoiceProcessing.Infrastructure.Import;

/// <summary>
/// File-level import used by the user interface: single files chosen by the user (they stay where they are)
/// and the defined inbox folder (files are cleaned up after processing).
/// </summary>
public sealed class InvoiceImportService(InvoiceProcessor processor, IOptions<StorageOptions> options, TimeProvider timeProvider)
{
    public static readonly string[] SupportedExtensions = [".pdf", ".xml"];

    public string InboxPath => options.Value.InboxPath;

    public async Task<ProcessingOutcome> ImportFileAsync(string path, CancellationToken ct)
    {
        InvoiceDocument document;
        try
        {
            document = await InvoiceDocument.LoadAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProcessingOutcome(ProcessingStatus.Retry, $"Datei nicht lesbar: {ex.Message}");
        }

        return await processor.ProcessAsync(document, ct);
    }

    public IReadOnlyList<string> GetInboxFiles()
    {
        Directory.CreateDirectory(InboxPath);
        return new DirectoryInfo(InboxPath)
            .EnumerateFiles()
            .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Name.StartsWith('~') && f.Extension != ".tmp")
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.FullName)
            .ToList();
    }

    /// <summary>
    /// Imports a file from the inbox, then removes it from there: imported → deleted (the original is in the archive),
    /// duplicate → Duplikate, error → Fehler with a .fehler.txt. On a temporary problem the file stays in the inbox.
    /// </summary>
    public async Task<ProcessingOutcome> ImportFromInboxAsync(string path, CancellationToken ct)
    {
        var outcome = await ImportFileAsync(path, ct);
        var settings = options.Value;

        switch (outcome.Status)
        {
            case ProcessingStatus.Imported:
                File.Delete(path);
                break;
            case ProcessingStatus.Duplicate:
                MoveTo(path, settings.DuplicatesPath);
                break;
            case ProcessingStatus.ValidationFailed:
            case ProcessingStatus.Failed:
                var target = MoveTo(path, settings.FailedPath);
                await File.WriteAllTextAsync(target + ".fehler.txt", ErrorReport(target, outcome), Encoding.UTF8, ct);
                break;
        }

        return outcome;
    }

    private string MoveTo(string path, string folder)
    {
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(path);
        var target = Path.Combine(folder, name);
        if (File.Exists(target))
        {
            var stamp = timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss");
            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}_{stamp}{Path.GetExtension(name)}");
        }

        File.Move(path, target);
        return target;
    }

    private string ErrorReport(string file, ProcessingOutcome outcome)
    {
        var report = new StringBuilder()
            .AppendLine($"Datei:     {Path.GetFileName(file)}")
            .AppendLine($"Zeitpunkt: {timeProvider.GetLocalNow():dd.MM.yyyy HH:mm:ss}")
            .AppendLine($"Ergebnis:  {outcome.Message}");
        foreach (var error in outcome.Errors)
            report.AppendLine($"  - {error}");
        return report.ToString();
    }
}
