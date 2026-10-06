using InvoiceProcessing.Core.Reading;
using s2industries.ZUGFeRD;

namespace InvoiceProcessing.Infrastructure.Readers;

/// <summary>XRechnung or other EN 16931 e-invoices delivered as plain XML (UBL or CII syntax). No AI involved.</summary>
public sealed class XmlInvoiceReader : IInvoiceReader
{
    public bool CanRead(InvoiceDocument document) => document.Extension == ".xml";

    public Task<ExtractionResult> ReadAsync(InvoiceDocument document, CancellationToken ct)
    {
        var syntax = ZugferdMapper.DetectSyntax(document.Content)
                     ?? throw new InvoiceReadException("XML-Datei ist keine bekannte E-Rechnung (weder UBL noch CII).");

        var descriptor = ZugferdMapper.Load(document.Content);
        var kind = descriptor.Profile is Profile.XRechnung or Profile.XRechnung1 ? "XRechnung" : "E-Rechnung";

        return Task.FromResult(new ExtractionResult(
            ZugferdMapper.ToInvoice(descriptor),
            $"{kind} {syntax}",
            $"{kind} ({syntax}-XML) – strukturiert ausgelesen, ohne KI"));
    }
}
