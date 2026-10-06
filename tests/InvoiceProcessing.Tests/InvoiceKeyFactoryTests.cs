using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Processing;

namespace InvoiceProcessing.Tests;

public class InvoiceKeyFactoryTests
{
    [Fact]
    public void Vat_id_is_normalized()
    {
        var fromXml = InvoiceKeyFactory.SupplierKey(new Party("Kaltenbrunn Bürobedarf GmbH", "DE811234567", null, null, null));
        var fromPdf = InvoiceKeyFactory.SupplierKey(new Party("Kaltenbrunn Bürobedarf", "de 811 234 567", null, null, null));

        Assert.Equal(fromXml, fromPdf);
    }

    [Fact]
    public void Name_is_used_without_vat_id()
    {
        Assert.Equal("NAME:KALTENBRUNNBÜROBEDARFGMBH", InvoiceKeyFactory.SupplierKey(new Party("Kaltenbrunn Bürobedarf GmbH", null, null, null, null)));
    }

    [Fact]
    public void Invoice_number_ignores_spaces_and_separators()
    {
        Assert.Equal(InvoiceKeyFactory.InvoiceNumberKey("RE-2026-10471"), InvoiceKeyFactory.InvoiceNumberKey("re 2026 10471"));
    }
}
