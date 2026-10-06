using InvoiceProcessing.Core.Reading;
using InvoiceProcessing.Infrastructure.Readers;

namespace InvoiceProcessing.Tests;

public class ReaderTests
{
    private static async Task<InvoiceDocument> Load(string prefix) => await InvoiceDocument.LoadAsync(TestData.File(prefix));

    [Fact]
    public async Task XRechnung_UBL_is_read_without_ai()
    {
        var result = await new XmlInvoiceReader().ReadAsync(await Load("01_"), CancellationToken.None);

        Assert.Equal("XRechnung UBL", result.Format);
        Assert.Equal("RE-2026-10471", result.Invoice.InvoiceNumber);
        Assert.Equal("DE811234567", result.Invoice.Supplier.VatId);
        Assert.Equal(4, result.Invoice.Lines.Count);
        Assert.Equal(878.70m, result.Invoice.GrossTotal);
    }

    [Fact]
    public async Task XRechnung_CII_with_line_and_document_discounts_is_read()
    {
        var invoice = (await new XmlInvoiceReader().ReadAsync(await Load("02_"), CancellationToken.None)).Invoice;

        Assert.Equal(14.64m, invoice.AllowanceTotal);
        Assert.Equal(717.22m, invoice.NetTotal);
        Assert.Equal(51.60m, invoice.Lines[0].DiscountAmount);
        Assert.Equal(10m, invoice.Lines[0].DiscountPercent);
        Assert.Equal(464.40m, invoice.Lines[0].NetAmount);
    }

    [Fact]
    public async Task Zugferd_pdf_is_detected_and_read_from_the_embedded_xml()
    {
        var reader = new ZugferdPdfReader();
        var document = await Load("03_");

        Assert.True(reader.CanRead(document));
        var result = await reader.ReadAsync(document, CancellationToken.None);
        Assert.Equal("ZUGFeRD PDF", result.Format);
        Assert.Equal(582.42m, result.Invoice.GrossTotal);
        Assert.All(result.Invoice.Lines, l => Assert.Equal(7m, l.VatRate));
    }

    [Theory]
    [InlineData("04_")]
    [InlineData("05_")]
    public async Task Plain_and_scanned_pdfs_are_left_to_openai(string prefix)
    {
        var document = await Load(prefix);

        Assert.False(new ZugferdPdfReader().CanRead(document));
        Assert.False(new XmlInvoiceReader().CanRead(document));
    }

    [Fact]
    public async Task Xml_that_is_not_an_invoice_is_rejected()
    {
        var document = InvoiceDocument.FromBytes("bestellung.xml", "<Order><Id>1</Id></Order>"u8.ToArray());

        await Assert.ThrowsAsync<InvoiceReadException>(() => new XmlInvoiceReader().ReadAsync(document, CancellationToken.None));
    }
}
