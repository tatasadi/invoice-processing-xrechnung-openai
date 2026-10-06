using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Infrastructure.Import;
using InvoiceProcessing.Infrastructure.Persistence;
using InvoiceProcessing.Infrastructure.Readers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessing.App.ViewModels;

public sealed partial class MainViewModel(
    InvoiceImportService importService,
    IInvoiceQueries queries,
    IDocumentArchive archive,
    DatabaseInitializer databaseInitializer,
    IOptions<OpenAiOptions> openAiOptions,
    IOptions<StorageOptions> storageOptions,
    ILogger<MainViewModel> logger) : ObservableObject
{
    private const int ListLimit = 500;

    public ObservableCollection<ImportItemViewModel> Imports { get; } = [];
    public ObservableCollection<StoredInvoice> Invoices { get; } = [];
    public ObservableCollection<InvoiceLine> SelectedInvoiceLines { get; } = [];
    public ObservableCollection<ProcessingLogRow> LogEntries { get; } = [];

    [ObservableProperty]
    private ImportItemViewModel? _selectedImport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOriginalCommand))]
    private StoredInvoice? _selectedInvoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ImportInboxCommand), nameof(RefreshCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMaximum = 1;

    [ObservableProperty]
    private bool _isDatabaseOnline;

    [ObservableProperty]
    private string _databaseStatus = "Datenbank wird verbunden …";

    [ObservableProperty]
    private string? _databaseError;

    public string ModelName => openAiOptions.Value.Model;
    public string InboxPath => storageOptions.Value.InboxPath;
    public string ArchivePath => storageOptions.Value.ArchivePath;

    public async Task InitializeAsync() => await ConnectDatabaseAsync();

    [RelayCommand]
    private async Task ConnectDatabaseAsync()
    {
        DatabaseStatus = "Datenbank wird verbunden …";
        try
        {
            var info = await Task.Run(() => databaseInitializer.InitializeAsync(CancellationToken.None));
            IsDatabaseOnline = true;
            DatabaseStatus = $"Datenbank verbunden · {info}";
            DatabaseError = null;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Datenbankverbindung fehlgeschlagen");
            IsDatabaseOnline = false;
            DatabaseStatus = "Datenbank nicht erreichbar";
            DatabaseError = ex.Message;
        }
    }

    /// <summary>Files chosen in the dialog or dropped on the window; folders are expanded.</summary>
    public Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var files = paths
            .SelectMany(path => Directory.Exists(path) ? Directory.GetFiles(path) : [path])
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return RunImportAsync(files, fromInbox: false);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommands))]
    private async Task ImportInboxAsync()
    {
        var files = importService.GetInboxFiles();
        if (files.Count == 0)
        {
            ProgressText = $"Der Eingangsordner ist leer ({InboxPath}).";
            return;
        }

        await RunImportAsync(files, fromInbox: true);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommands))]
    private async Task RefreshAsync()
    {
        try
        {
            var invoices = await Task.Run(() => queries.ListInvoicesAsync(ListLimit, CancellationToken.None));
            var log = await Task.Run(() => queries.ListLogAsync(ListLimit, CancellationToken.None));

            var selectedId = SelectedInvoice?.Id;
            Replace(Invoices, invoices);
            Replace(LogEntries, log);
            SelectedInvoice = Invoices.FirstOrDefault(i => i.Id == selectedId) ?? Invoices.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Laden der Rechnungen fehlgeschlagen");
            DatabaseError = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenOriginal))]
    private void OpenOriginal()
    {
        if (SelectedInvoice?.ArchivePath is not { } relativePath) return;
        OpenInShell(archive.GetFullPath(relativePath));
    }

    [RelayCommand]
    private void OpenInboxFolder()
    {
        Directory.CreateDirectory(InboxPath);
        OpenInShell(InboxPath);
    }

    [RelayCommand]
    private void OpenImportedFile(ImportItemViewModel? item)
    {
        if (item is not null && File.Exists(item.FilePath)) OpenInShell(item.FilePath);
    }

    private bool CanRunCommands() => !IsBusy;

    private bool CanOpenOriginal() => SelectedInvoice?.ArchivePath is not null;

    partial void OnSelectedInvoiceChanged(StoredInvoice? value) => _ = LoadLinesAsync(value);

    private async Task RunImportAsync(IReadOnlyList<string> files, bool fromInbox)
    {
        if (IsBusy || files.Count == 0) return;
        IsBusy = true;

        var items = files.Select(file => new ImportItemViewModel(file)).ToList();
        for (var i = 0; i < items.Count; i++) Imports.Insert(i, items[i]);

        ProgressMaximum = items.Count;
        ProgressValue = 0;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.State = ImportState.Processing;
                SelectedImport = item;
                ProgressText = $"Verarbeite {i + 1} von {items.Count}: {item.FileName}";

                var outcome = await Task.Run(() => fromInbox
                    ? importService.ImportFromInboxAsync(item.FilePath, CancellationToken.None)
                    : importService.ImportFileAsync(item.FilePath, CancellationToken.None));
                item.Complete(outcome);
                ProgressValue = i + 1;
            }
        }
        finally
        {
            IsBusy = false;
        }

        ProgressText = Summary(items);
        if (IsDatabaseOnline) await RefreshAsync();
        else if (items.Any(i => i.State == ImportState.Retry)) await ConnectDatabaseAsync();
    }

    private async Task LoadLinesAsync(StoredInvoice? invoice)
    {
        SelectedInvoiceLines.Clear();
        if (invoice is null) return;
        try
        {
            var lines = await Task.Run(() => queries.ListLinesAsync(invoice.Id, CancellationToken.None));
            if (SelectedInvoice?.Id == invoice.Id) Replace(SelectedInvoiceLines, lines);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Laden der Positionen fehlgeschlagen");
        }
    }

    private static string Summary(IReadOnlyList<ImportItemViewModel> items)
    {
        int Count(ImportState state) => items.Count(i => i.State == state);
        var parts = new List<string> { $"{Count(ImportState.Imported)} importiert" };
        if (Count(ImportState.Duplicate) > 0) parts.Add($"{Count(ImportState.Duplicate)} Duplikat(e)");
        if (Count(ImportState.Failed) > 0) parts.Add($"{Count(ImportState.Failed)} mit Fehler");
        if (Count(ImportState.Retry) > 0) parts.Add($"{Count(ImportState.Retry)} nicht verarbeitet");
        return $"{items.Count} Datei(en) verarbeitet: {string.Join(", ", parts)}.";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private static void OpenInShell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
