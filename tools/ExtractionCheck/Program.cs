// Reads invoice files without a database and prints what was extracted and whether it passes validation.
// Useful to check the readers and the OpenAI prompt: dotnet run --project tools/ExtractionCheck -- testdata/*.pdf

using System.Globalization;
using System.Text;
using InvoiceProcessing.Core.Reading;
using InvoiceProcessing.Core.Validation;
using InvoiceProcessing.Infrastructure.Readers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;
var german = CultureInfo.GetCultureInfo("de-DE");

var configuration = new ConfigurationBuilder()
    .AddUserSecrets("invoice-processing")
    .AddEnvironmentVariables()
    .Build();
var openAi = new OpenAiOptions();
configuration.GetSection("OpenAI").Bind(openAi);

IInvoiceReader[] readers =
[
    new XmlInvoiceReader(),
    new ZugferdPdfReader(),
    new OpenAiPdfReader(Options.Create(openAi), NullLogger<OpenAiPdfReader>.Instance),
];
var validator = new InvoiceValidator(new ValidationOptions(), TimeProvider.System);

var files = args.SelectMany(arg => Directory.Exists(arg)
        ? Directory.GetFiles(arg)
        : Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(arg))!, Path.GetFileName(arg)))
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToList();

foreach (var file in files)
{
    Console.WriteLine($"=== {Path.GetFileName(file)}");
    var document = await InvoiceDocument.LoadAsync(file);
    var reader = readers.FirstOrDefault(r => r.CanRead(document));
    if (reader is null)
    {
        Console.WriteLine("    kein passender Reader");
        continue;
    }

    try
    {
        var result = await reader.ReadAsync(document, CancellationToken.None);
        var invoice = result.Invoice;
        Console.WriteLine($"    {result.Description}");
        Console.WriteLine($"    {invoice.Supplier.Name} | USt-IdNr. {invoice.Supplier.VatId} | {invoice.Supplier.Street}, {invoice.Supplier.PostalCode} {invoice.Supplier.City}");
        Console.WriteLine($"    Nr. {invoice.InvoiceNumber} vom {invoice.InvoiceDate:dd.MM.yyyy}, fällig {invoice.DueDate:dd.MM.yyyy}, Bestellung {invoice.OrderReference}");
        foreach (var l in invoice.Lines)
        {
            Console.WriteLine($"    {l.LineNumber,2} {l.ArticleNumber,-12} {l.Description,-46} {l.Quantity,6:0.##} +{l.FreeQuantity,3:0.##} {l.Unit,-6} "
                              + $"{l.UnitPrice.ToString("N2", german),8} {l.DiscountPercent,4:0.##}% {l.DiscountAmount?.ToString("N2", german),7} "
                              + $"{l.NetAmount.ToString("N2", german),9} {l.VatRate,3:0}%");
        }

        Console.WriteLine($"    Abschläge {invoice.AllowanceTotal.ToString("N2", german)} | Netto {invoice.NetTotal.ToString("N2", german)} | "
                          + $"USt {invoice.TaxTotal.ToString("N2", german)} | Brutto {invoice.GrossTotal.ToString("N2", german)} {invoice.Currency}");
        var errors = validator.Validate(invoice);
        Console.WriteLine(errors.Count == 0 ? "    Prüfung: OK" : "    Prüfung: FEHLER\n      - " + string.Join("\n      - ", errors));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    {ex.GetType().Name}: {ex.Message}");
    }
}
