using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Processing;

namespace InvoiceProcessing.App.ViewModels;

public enum ImportState
{
    Waiting,
    Processing,
    Imported,
    Duplicate,
    Failed,
    Retry,
}

/// <summary>One file of the current session in the import list, with what was read and the result.</summary>
public sealed partial class ImportItemViewModel(string filePath) : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public string FilePath { get; } = filePath;
    public string FileName { get; } = Path.GetFileName(filePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ImportState _state = ImportState.Waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Invoice), nameof(Lines), nameof(Errors), nameof(Message), nameof(StatusText), nameof(Summary),
        nameof(Description), nameof(DurationText), nameof(MetaText), nameof(SupplierAddress))]
    private ProcessingOutcome? _outcome;

    public Invoice? Invoice => Outcome?.Invoice;
    public IReadOnlyList<InvoiceLine> Lines => Outcome?.Invoice?.Lines ?? [];
    public IReadOnlyList<string> Errors => Outcome?.Errors ?? [];
    public string Message => Outcome?.Message ?? (State == ImportState.Processing ? "Wird verarbeitet …" : "Wartet …");
    public string? Description => Outcome?.Description;

    public string StatusText => State switch
    {
        ImportState.Waiting => "Wartet",
        ImportState.Processing => "Wird verarbeitet …",
        ImportState.Imported => "Importiert",
        ImportState.Duplicate => "Duplikat",
        ImportState.Retry => "Nicht verarbeitet",
        _ => Outcome?.Status == ProcessingStatus.ValidationFailed ? "Prüfung fehlgeschlagen" : "Fehler",
    } + (Outcome?.Format is { } format ? $" · {format}" : "");

    public string? Summary => Invoice is { } invoice
        ? $"{invoice.Supplier.Name} · {invoice.InvoiceNumber} · {invoice.GrossTotal.ToString("N2", German)} {invoice.Currency}"
        : null;

    public string? SupplierAddress => Invoice?.Supplier is { } s
        ? string.Join(", ", new[] { s.Street, $"{s.PostalCode} {s.City}".Trim() }.Where(p => !string.IsNullOrWhiteSpace(p)))
        : null;

    public string? DurationText => Outcome?.Duration switch
    {
        null => null,
        { TotalSeconds: < 1 } duration => $"{duration.TotalMilliseconds:0} ms",
        { } duration => $"{duration.TotalSeconds.ToString("0.0", German)} s",
    };

    /// <summary>File name, how it was read and how long it took, e.g. "02.xml · XRechnung (CII-XML) – … · 25 ms".</summary>
    public string MetaText => string.Join("  ·  ", new[] { FileName, Description, DurationText }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public void Complete(ProcessingOutcome outcome)
    {
        Outcome = outcome;
        State = outcome.Status switch
        {
            ProcessingStatus.Imported => ImportState.Imported,
            ProcessingStatus.Duplicate => ImportState.Duplicate,
            ProcessingStatus.Retry => ImportState.Retry,
            _ => ImportState.Failed,
        };
    }
}
