using System.Xml;
using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Reading;
using s2industries.ZUGFeRD;
using Party = InvoiceProcessing.Core.Model.Party;

namespace InvoiceProcessing.Infrastructure.Readers;

/// <summary>
/// Maps structured e-invoices (XRechnung UBL/CII, ZUGFeRD/Factur-X) to the internal model.
/// Parsing is done by the open-source library ZUGFeRD-csharp (Apache 2.0).
/// </summary>
internal static class ZugferdMapper
{
    public static string? DetectSyntax(byte[] xml)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            reader.MoveToContent();
            return reader.LocalName switch
            {
                "Invoice" or "CreditNote" => "UBL",
                "CrossIndustryInvoice" or "CrossIndustryDocument" => "CII",
                _ => null,
            };
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public static InvoiceDescriptor Load(byte[] xml)
    {
        try
        {
            using var stream = new MemoryStream(xml);
            return InvoiceDescriptor.Load(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvoiceReadException($"E-Rechnung konnte nicht gelesen werden: {ex.Message}", ex);
        }
    }

    public static Invoice ToInvoice(InvoiceDescriptor d)
    {
        var lines = d.TradeLineItems
            .Where(item => item.LineTotalAmount.HasValue || item.BilledQuantity != 0) // skip pure comment lines
            .Select(ToLine)
            .ToList();

        var lineTotal = d.LineTotalAmount ?? lines.Sum(l => l.NetAmount);
        var allowances = d.AllowanceTotalAmount ?? 0m;
        var charges = d.ChargeTotalAmount ?? 0m;
        var net = d.TaxBasisAmount ?? lineTotal - allowances + charges;
        var tax = d.TaxTotalAmount ?? d.Taxes.Sum(t => t.TaxAmount);
        var dueDate = d.PaymentTerms?.Select(t => t.DueDate).FirstOrDefault(date => date.HasValue);

        return new Invoice
        {
            InvoiceNumber = d.InvoiceNo ?? "",
            InvoiceDate = d.InvoiceDate is { } invoiceDate
                ? DateOnly.FromDateTime(invoiceDate)
                : throw new InvoiceReadException("Rechnungsdatum fehlt in der E-Rechnung."),
            DueDate = dueDate is { } due ? DateOnly.FromDateTime(due) : null,
            Currency = d.Currency.ToString(),
            Supplier = new Party(
                d.Seller?.Name ?? "",
                d.SellerTaxRegistration?.FirstOrDefault(r => r.SchemeID == TaxRegistrationSchemeID.VA)?.No,
                d.Seller?.Street,
                d.Seller?.Postcode,
                d.Seller?.City),
            OrderReference = d.OrderNo,
            AllowanceTotal = allowances,
            ChargeTotal = charges,
            NetTotal = net,
            TaxTotal = tax,
            GrossTotal = d.GrandTotalAmount ?? net + tax,
            Lines = lines,
        };
    }

    private static InvoiceLine ToLine(TradeLineItem item, int index)
    {
        var allowances = item.GetSpecifiedTradeAllowances();
        var charges = item.GetSpecifiedTradeCharges();
        decimal? discount = allowances.Count + charges.Count > 0
            ? allowances.Sum(a => a.ActualAmount) - charges.Sum(c => c.ActualAmount)
            : null;
        decimal? discountPercent = allowances.Count == 1 && charges.Count == 0 ? allowances[0].ChargePercentage : null;

        // The price can refer to a base quantity (e.g. price per 100 pieces).
        var baseQuantity = item.NetQuantity is > 0 ? item.NetQuantity.Value : 1m;
        var unitPrice = item.NetUnitPrice / baseQuantity;

        return new InvoiceLine
        {
            LineNumber = int.TryParse(item.AssociatedDocument?.LineID, out var number) ? number : index + 1,
            ArticleNumber = item.SellerAssignedID ?? item.GlobalID?.ID,
            Description = item.Name ?? "",
            Quantity = item.BilledQuantity,
            FreeQuantity = item.ChargeFreeQuantity ?? 0m,
            Unit = UnitText(item.UnitCode),
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
            DiscountAmount = discount,
            NetAmount = item.LineTotalAmount ?? Math.Round(item.BilledQuantity * unitPrice - (discount ?? 0m), 2),
            VatRate = item.TaxPercent,
        };
    }

    private static string? UnitText(QuantityCodes? code) => code switch
    {
        null => null,
        QuantityCodes.H87 or QuantityCodes.C62 => "Stk",
        QuantityCodes.KGM => "kg",
        QuantityCodes.LTR => "l",
        QuantityCodes.XBX => "Karton",
        QuantityCodes.XCS or QuantityCodes.XCR => "Kiste",
        QuantityCodes.XPK => "Pack",
        QuantityCodes.XRO => "Rolle",
        QuantityCodes.HUR => "Std",
        _ => code.ToString(),
    };
}
