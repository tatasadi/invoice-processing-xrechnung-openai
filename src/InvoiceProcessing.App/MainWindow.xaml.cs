using System.Windows;
using System.Windows.Media;
using InvoiceProcessing.App.ViewModels;
using Microsoft.Win32;

namespace InvoiceProcessing.App;

public partial class MainWindow : Window
{
    private static readonly Brush DropHighlight = new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xF8));
    private static readonly Brush DropHighlightBorder = new SolidColorBrush(Color.FromRgb(0x0F, 0x5C, 0x8C));

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private async void OnSelectFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Rechnungen auswählen",
            Multiselect = true,
            Filter = "Rechnungen (*.pdf;*.xml)|*.pdf;*.xml|Alle Dateien (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
            await ViewModel.ImportFilesAsync(dialog.FileNames);
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!CanAccept(e)) return;
        DropZone.Background = DropHighlight;
        DropBorder.Stroke = DropHighlightBorder;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAccept(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ResetDropZone();

    private async void OnDrop(object sender, DragEventArgs e)
    {
        ResetDropZone();
        if (CanAccept(e) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await ViewModel.ImportFilesAsync(paths);
    }

    private void OnImportSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ImportList.SelectedItem is { } item) ImportList.ScrollIntoView(item);
        DetailScroll.ScrollToTop();
    }

    private bool CanAccept(DragEventArgs e) => ViewModel.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop);

    private void ResetDropZone()
    {
        DropZone.ClearValue(BackgroundProperty);
        DropZone.Background = (Brush)FindResource("CardBackground");
        DropBorder.ClearValue(System.Windows.Shapes.Shape.StrokeProperty);
        DropBorder.Stroke = new SolidColorBrush(Color.FromRgb(0x9D, 0xB7, 0xCC));
    }
}
