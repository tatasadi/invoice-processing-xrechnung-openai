// Generates the fictitious test invoices into testdata/.
// QuestPDF (community license) is only used here, to draw the test PDFs; the application itself does not use it.

using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using s2industries.ZUGFeRD;
using s2industries.ZUGFeRD.PDF;
using TestInvoiceGenerator;

QuestPDF.Settings.License = LicenseType.Community;

var outputDirectory = args.Length > 0 ? args[0] : Path.Combine(FindRepositoryRoot(), "testdata");
Directory.CreateDirectory(outputDirectory);
string PathOf(InvoiceSpec spec, string extension) => Path.Combine(outputDirectory, spec.FileName + extension);

// 01 XRechnung as UBL XML
EInvoiceFactory.Create(InvoiceSpecs.KaltenbrunnInvoice)
    .Save(PathOf(InvoiceSpecs.KaltenbrunnInvoice, ".xml"), ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.UBL);

// 02 XRechnung as CII XML (line discounts + loyalty discount on the whole invoice)
EInvoiceFactory.Create(InvoiceSpecs.HofstetterInvoice)
    .Save(PathOf(InvoiceSpecs.HofstetterInvoice, ".xml"), ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.CII);

// 03 ZUGFeRD: visual PDF with the CII XML embedded
var visualPdf = Path.Combine(Path.GetTempPath(), $"zugferd-source-{Guid.NewGuid():N}.pdf");
InvoicePdfRenderer.Create(InvoiceSpecs.LindmayrInvoice,
        eInvoiceNote: "Diese Rechnung enthält die Rechnungsdaten zusätzlich als ZUGFeRD-E-Rechnung (eingebettete XML).")
    .GeneratePdf(visualPdf);
InvoicePdfProcessor.SaveToPdf(PathOf(InvoiceSpecs.LindmayrInvoice, ".pdf"), ZUGFeRDVersion.Version23, Profile.Comfort,
    ZUGFeRDFormats.CII, visualPdf, EInvoiceFactory.Create(InvoiceSpecs.LindmayrInvoice));
File.Delete(visualPdf);

// 04 plain PDF: free goods, line discounts, 19 % and 7 % VAT
InvoicePdfRenderer.Create(InvoiceSpecs.BrennwaldInvoice).GeneratePdf(PathOf(InvoiceSpecs.BrennwaldInvoice, ".pdf"));

// 05 scanned PDF: image only (no text layer), slightly rotated, with a receipt stamp; bonus quantity as a separate line
var pageImages = InvoicePdfRenderer.Create(InvoiceSpecs.SeidlInvoice, receivedStamp: "29. SEP. 2026")
    .GenerateImages(new ImageGenerationSettings
    {
        ImageFormat = ImageFormat.Jpeg,
        ImageCompressionQuality = ImageCompressionQuality.Medium,
        RasterDpi = 150,
    })
    .ToList();
Document.Create(container =>
{
    foreach (var image in pageImages)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor("#ECE9E1");
            page.Content().Padding(6).Rotate(0.6f).Image(image).FitArea();
        });
    }
}).GeneratePdf(PathOf(InvoiceSpecs.SeidlInvoice, ".pdf"));

// 06 plain PDF with a wrong printed net total
InvoicePdfRenderer.Create(InvoiceSpecs.HubertusInvoice).GeneratePdf(PathOf(InvoiceSpecs.HubertusInvoice, ".pdf"));

// 07 the invoice of 01 again, as plain PDF
InvoicePdfRenderer.Create(InvoiceSpecs.KaltenbrunnPdfCopy).GeneratePdf(PathOf(InvoiceSpecs.KaltenbrunnPdfCopy, ".pdf"));

var german = CultureInfo.GetCultureInfo("de-DE");
foreach (var spec in new[]
         {
             InvoiceSpecs.KaltenbrunnInvoice, InvoiceSpecs.HofstetterInvoice, InvoiceSpecs.LindmayrInvoice, InvoiceSpecs.BrennwaldInvoice,
             InvoiceSpecs.SeidlInvoice, InvoiceSpecs.HubertusInvoice, InvoiceSpecs.KaltenbrunnPdfCopy,
         })
{
    var calc = InvoiceCalculation.For(spec);
    Console.WriteLine($"{spec.FileName,-32} {spec.Number,-16} Positionen {calc.LineTotal.ToString("N2", german),10}  "
                      + $"netto {calc.Net.ToString("N2", german),10}  USt {calc.Tax.ToString("N2", german),8}  brutto {calc.Gross.ToString("N2", german),10}");
}

Console.WriteLine($"Testrechnungen erstellt in {outputDirectory}");

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (directory.GetFiles("*.slnx").Length > 0) return directory.FullName;
    }

    throw new InvalidOperationException("Repository root (*.slnx) not found.");
}
