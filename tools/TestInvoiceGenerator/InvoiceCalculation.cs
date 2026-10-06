namespace TestInvoiceGenerator;

internal sealed record LineCalculation(int Number, LineSpec Line, decimal Gross, decimal Discount, decimal Net);

internal sealed record TaxGroup(decimal Rate, decimal Basis, decimal Tax);

internal sealed record InvoiceCalculation(
    IReadOnlyList<LineCalculation> Lines,
    decimal LineTotal,
    decimal Allowance,
    decimal Net,
    IReadOnlyList<TaxGroup> Taxes,
    decimal Tax,
    decimal Gross)
{
    public static InvoiceCalculation For(InvoiceSpec spec)
    {
        var lines = spec.Lines.Select((line, index) =>
        {
            var gross = Round(line.Quantity * line.Price);
            var discount = Round(gross * line.DiscountPercent / 100m);
            return new LineCalculation(index + 1, line, gross, discount, gross - discount);
        }).ToList();

        var lineTotal = lines.Sum(l => l.Net);
        var allowance = Round(lineTotal * spec.AllowancePercent / 100m);
        if (allowance != 0 && lines.Select(l => l.Line.VatRate).Distinct().Count() > 1)
            throw new InvalidOperationException("Document-level allowance is only supported for a single VAT rate.");

        // A deliberately wrong printed net total (test case for validation) also drives the printed VAT and gross amount.
        var net = spec.PrintedNetOverride ?? lineTotal - allowance;
        var taxes = spec.PrintedNetOverride is null
            ? lines.GroupBy(l => l.Line.VatRate)
                .OrderByDescending(g => g.Key)
                .Select(g =>
                {
                    var basis = g.Sum(l => l.Net) - allowance;
                    return new TaxGroup(g.Key, basis, Round(basis * g.Key / 100m));
                })
                .ToList()
            : [new TaxGroup(lines[0].Line.VatRate, net, Round(net * lines[0].Line.VatRate / 100m))];

        var tax = taxes.Sum(t => t.Tax);
        return new InvoiceCalculation(lines, lineTotal, allowance, net, taxes, tax, net + tax);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
