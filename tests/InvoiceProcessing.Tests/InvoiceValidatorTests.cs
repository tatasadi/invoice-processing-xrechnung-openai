using InvoiceProcessing.Core.Validation;

namespace InvoiceProcessing.Tests;

public class InvoiceValidatorTests
{
    private readonly InvoiceValidator _validator = new(new ValidationOptions(), TimeProvider.System);

    [Fact]
    public void Consistent_invoice_passes()
    {
        var invoice = TestData.Invoice(TestData.Line(10, 4.29m, 42.90m), TestData.Line(4, 129m, 464.40m, discountPercent: 10, number: 2));

        Assert.Empty(_validator.Validate(invoice));
    }

    [Fact]
    public void Printed_net_total_that_differs_from_the_lines_is_rejected()
    {
        var invoice = TestData.Invoice(TestData.Line(50, 6.90m, 345.00m)) with { NetTotal = 354.00m, TaxTotal = 67.26m, GrossTotal = 421.26m };

        var errors = _validator.Validate(invoice);

        Assert.Contains(errors, e => e.Contains("Summe der Positionen 345,00 ≠ Nettobetrag 354,00"));
    }

    [Fact]
    public void Wrong_line_arithmetic_is_rejected()
    {
        var invoice = TestData.Invoice(TestData.Line(10, 4.29m, 49.20m));

        var errors = _validator.Validate(invoice);

        Assert.Contains(errors, e => e.StartsWith("Position 1:"));
    }

    [Fact]
    public void Net_plus_tax_must_equal_gross()
    {
        var invoice = TestData.Invoice(TestData.Line(10, 4.29m, 42.90m)) with { GrossTotal = 60m };

        Assert.Contains(_validator.Validate(invoice), e => e.StartsWith("Netto"));
    }

    [Fact]
    public void Tax_is_checked_per_rate_for_mixed_rates()
    {
        var invoice = TestData.Invoice(TestData.Line(10, 10m, 100m), TestData.Line(10, 10m, 100m, vat: 7, number: 2))
            with { TaxTotal = 38m, GrossTotal = 238m };

        Assert.Contains(_validator.Validate(invoice), e => e.StartsWith("USt laut Steuersätzen 26,00"));
    }

    [Fact]
    public void Document_level_allowance_is_taken_into_account()
    {
        var invoice = TestData.Invoice(TestData.Line(10, 73.186m, 731.86m)) with
        {
            AllowanceTotal = 14.64m,
            NetTotal = 717.22m,
            TaxTotal = 136.27m,
            GrossTotal = 853.49m,
        };

        Assert.Empty(_validator.Validate(invoice));
    }

    [Fact]
    public void Free_goods_line_with_price_zero_passes()
    {
        var line = TestData.Line(0, 0m, 0m) with { FreeQuantity = 50 };
        var invoice = TestData.Invoice(TestData.Line(500, 0.62m, 310m), line with { LineNumber = 2 });

        Assert.Empty(_validator.Validate(invoice));
    }

    [Fact]
    public void Missing_number_and_future_date_are_reported()
    {
        var invoice = TestData.Invoice(TestData.Line(1, 1m, 1m)) with { InvoiceNumber = " ", InvoiceDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)) };

        var errors = _validator.Validate(invoice);

        Assert.Contains("Rechnungsnummer fehlt.", errors);
        Assert.Contains(errors, e => e.Contains("liegt in der Zukunft"));
    }
}
