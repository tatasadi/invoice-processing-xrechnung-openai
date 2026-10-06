using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using InvoiceProcessing.App.ViewModels;

namespace InvoiceProcessing.App.Converters;

/// <summary>
/// Icon glyph (Segoe Fluent Icons) and colors per status. Works for <see cref="ImportState"/> and for the
/// status codes stored in the processing log ("imported", "duplicate", ...). ConverterParameter: Glyph, Foreground, Background.
/// </summary>
public sealed class StatusVisualConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value switch
        {
            ImportState s => s,
            string code => ImportStateFromLog(code),
            _ => ImportState.Waiting,
        };

        return (parameter as string) switch
        {
            "Glyph" => state switch
            {
                ImportState.Imported => "",   // check mark
                ImportState.Duplicate => "",  // copy
                ImportState.Failed => "",     // cancel
                ImportState.Retry => "",      // refresh
                ImportState.Processing => "", // sync
                _ => "",                      // clock
            },
            "Background" => Brush(state switch
            {
                ImportState.Imported => "#DFF6DD",
                ImportState.Duplicate => "#FFF4CE",
                ImportState.Failed => "#FDE7E9",
                ImportState.Retry or ImportState.Processing => "#E6F2FB",
                _ => "#F0F0F0",
            }),
            _ => Brush(state switch
            {
                ImportState.Imported => "#107C10",
                ImportState.Duplicate => "#8A5300",
                ImportState.Failed => "#C42B1C",
                ImportState.Retry or ImportState.Processing => "#005FB8",
                _ => "#6B6B6B",
            }),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    public static ImportState ImportStateFromLog(string code) => code switch
    {
        "imported" => ImportState.Imported,
        "duplicate" => ImportState.Duplicate,
        "validation_failed" or "failed" => ImportState.Failed,
        _ => ImportState.Waiting,
    };

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Status codes of the processing log in German.</summary>
public sealed class LogStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "imported" => "Importiert",
        "duplicate" => "Duplikat",
        "validation_failed" => "Prüfung fehlgeschlagen",
        "failed" => "Fehler",
        _ => value ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Null, empty string or empty collection → Collapsed. ConverterParameter "Invert" turns it around.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int count => count == 0,
            decimal number => number == 0,
            System.Collections.ICollection collection => collection.Count == 0,
            bool flag => !flag,
            _ => false,
        };
        if (parameter as string == "Invert") isEmpty = !isEmpty;
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>UTC timestamps from the database → local time.</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime time ? DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime() : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
