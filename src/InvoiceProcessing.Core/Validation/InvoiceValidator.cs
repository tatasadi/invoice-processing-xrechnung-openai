using System.Globalization;
using InvoiceProcessing.Core.Model;

namespace InvoiceProcessing.Core.Validation;

public sealed class ValidationOptions
{
    /// <summary>Allowed rounding difference per check, in the invoice currency.</summary>
    public decimal AmountTolerance { get; set; } = 0.02m;

    public int MaxDaysInFuture { get; set; } = 1;
}

/// <summary>
/// Checks an extracted invoice before it is stored: required fields, line arithmetic, totals and VAT.
/// Especially important for data read by AI: an invoice that does not add up is never imported silently.
/// </summary>
public sealed class InvoiceValidator(ValidationOptions options, TimeProvider timeProvider)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public IReadOnlyList<string> Validate(Invoice invoice)
    {
        var errors = new List<string>();
        var tolerance = options.AmountTolerance;

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber)) errors.Add("Rechnungsnummer fehlt.");
        if (string.IsNullOrWhiteSpace(invoice.Supplier.Name)) errors.Add("Lieferant fehlt.");
        if (invoice.Currency is not { Length: 3 }) errors.Add($"Ungültige Währung \"{invoice.Currency}\".");

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        if (invoice.InvoiceDate > today.AddDays(options.MaxDaysInFuture))
            errors.Add($"Rechnungsdatum {invoice.InvoiceDate:dd.MM.yyyy} liegt in der Zukunft.");
        if (invoice.InvoiceDate.Year < 2000)
            errors.Add($"Rechnungsdatum {invoice.InvoiceDate:dd.MM.yyyy} ist nicht plausibel.");

        if (invoice.Lines.Count == 0) errors.Add("Keine Rechnungspositionen gefunden.");

        foreach (var line in invoice.Lines)
        {
            var label = $"Position {line.LineNumber}";
            if (string.IsNullOrWhiteSpace(line.Description)) errors.Add($"{label}: Bezeichnung fehlt.");
            if (line.Quantity < 0 || line.FreeQuantity < 0) errors.Add($"{label}: negative Menge.");

            var expected = ExpectedLineAmount(line);
            if (Math.Abs(expected - line.NetAmount) > tolerance)
                errors.Add($"{label}: Menge × Preis − Rabatt = {Money(expected)}, auf der Rechnung steht {Money(line.NetAmount)}.");
        }

        if (invoice.Lines.Count > 0)
        {
            var lineSum = invoice.Lines.Sum(l => l.NetAmount);
            var expectedNet = lineSum - invoice.AllowanceTotal + invoice.ChargeTotal;
            if (Math.Abs(expectedNet - invoice.NetTotal) > tolerance)
            {
                errors.Add(invoice.AllowanceTotal == 0 && invoice.ChargeTotal == 0
                    ? $"Summe der Positionen {Money(lineSum)} ≠ Nettobetrag {Money(invoice.NetTotal)}."
                    : $"Positionen {Money(lineSum)} − Abschläge {Money(invoice.AllowanceTotal)} + Zuschläge {Money(invoice.ChargeTotal)} "
                      + $"= {Money(expectedNet)} ≠ Nettobetrag {Money(invoice.NetTotal)}.");
            }
        }

        if (Math.Abs(invoice.NetTotal + invoice.TaxTotal - invoice.GrossTotal) > tolerance)
            errors.Add($"Netto {Money(invoice.NetTotal)} + USt {Money(invoice.TaxTotal)} ≠ Brutto {Money(invoice.GrossTotal)}.");

        if (ExpectedTax(invoice) is { } expectedTax && Math.Abs(expectedTax.Amount - invoice.TaxTotal) > tolerance * expectedTax.Groups)
            errors.Add($"USt laut Steuersätzen {Money(expectedTax.Amount)} ≠ USt auf der Rechnung {Money(invoice.TaxTotal)}.");

        return errors;
    }

    public static decimal ExpectedLineAmount(InvoiceLine line)
    {
        var gross = line.Quantity * line.UnitPrice;
        var discount = line.DiscountAmount
                       ?? (line.DiscountPercent is { } percent ? Round(gross * percent / 100m) : 0m);
        return Round(gross - discount);
    }

    /// <summary>VAT computed from the line rates; null when the lines do not carry rates.</summary>
    private static (decimal Amount, int Groups)? ExpectedTax(Invoice invoice)
    {
        if (invoice.Lines.Count == 0 || invoice.Lines.Any(l => l.VatRate is null)) return null;

        var groups = invoice.Lines.GroupBy(l => l.VatRate!.Value).ToList();
        if (groups.Count == 1) return (Round(invoice.NetTotal * groups[0].Key / 100m), 1);

        // Several rates and document-level allowances: the split per rate is not known, so skip the check.
        if (invoice.AllowanceTotal != 0 || invoice.ChargeTotal != 0) return null;

        return (groups.Sum(g => Round(g.Sum(l => l.NetAmount) * g.Key / 100m)), groups.Count);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Money(decimal value) => value.ToString("N2", German);
}
