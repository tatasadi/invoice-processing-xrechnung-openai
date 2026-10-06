using InvoiceProcessing.Core.Reading;
using UglyToad.PdfPig;

namespace InvoiceProcessing.Infrastructure.Readers;

/// <summary>
/// ZUGFeRD / Factur-X: a PDF with the invoice data embedded as XML. The XML is read directly, no AI involved.
/// PDF access via PdfPig (Apache 2.0).
/// </summary>
public sealed class ZugferdPdfReader : IInvoiceReader
{
    private static readonly string[] PreferredNames = ["factur-x.xml", "zugferd-invoice.xml", "xrechnung.xml"];

    public bool CanRead(InvoiceDocument document) =>
        document.Extension == ".pdf" && FindEmbeddedInvoiceXml(document.Content) is not null;

    public Task<ExtractionResult> ReadAsync(InvoiceDocument document, CancellationToken ct)
    {
        var xml = FindEmbeddedInvoiceXml(document.Content)
                  ?? throw new InvoiceReadException("PDF enthält keine eingebettete E-Rechnung.");

        var descriptor = ZugferdMapper.Load(xml.Content);

        return Task.FromResult(new ExtractionResult(
            ZugferdMapper.ToInvoice(descriptor),
            "ZUGFeRD PDF",
            $"ZUGFeRD / Factur-X (PDF mit eingebetteter {xml.Name}, Profil {descriptor.Profile}) – strukturiert ausgelesen, ohne KI"));
    }

    internal static EmbeddedXml? FindEmbeddedInvoiceXml(byte[] pdf)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            if (!document.Advanced.TryGetEmbeddedFiles(out var files)) return null;

            var candidates = files
                .Select(f => new EmbeddedXml(Path.GetFileName(f.FileSpecification ?? f.Name ?? ""), f.Memory.ToArray()))
                .Where(f => f.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Array.FindIndex(PreferredNames, n => n.Equals(f.Name, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : int.MaxValue);

            return candidates.FirstOrDefault(f => ZugferdMapper.DetectSyntax(f.Content) is not null);
        }
        catch (Exception)
        {
            return null; // not a readable PDF with attachments: leave it to the next reader
        }
    }

    internal sealed record EmbeddedXml(string Name, byte[] Content);
}
