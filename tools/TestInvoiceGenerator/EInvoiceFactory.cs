using s2industries.ZUGFeRD;

namespace TestInvoiceGenerator;

/// <summary>Builds EN 16931 e-invoice data (XRechnung / ZUGFeRD) for a spec with ZUGFeRD-csharp.</summary>
internal static class EInvoiceFactory
{
    public static InvoiceDescriptor Create(InvoiceSpec spec)
    {
        var calc = InvoiceCalculation.For(spec);
        var supplier = spec.Supplier;

        var d = InvoiceDescriptor.CreateInvoice(spec.Number, spec.Date.ToDateTime(TimeOnly.MinValue), CurrencyCodes.EUR);
        d.ReferenceOrderNo = spec.OrderNumber; // BT-10 buyer reference, mandatory in XRechnung
        d.SetBuyerOrderReferenceDocument(spec.OrderNumber);
        d.ActualDeliveryDate = spec.Date.ToDateTime(TimeOnly.MinValue);

        d.SetSeller(supplier.Name, supplier.PostalCode, supplier.City, supplier.Street, CountryCodes.DE);
        d.AddSellerTaxRegistration(supplier.VatId, TaxRegistrationSchemeID.VA);
        d.SetSellerContact(supplier.ManagingDirector, "Buchhaltung", supplier.Email, supplier.Phone);
        d.SetSellerElectronicAddress(supplier.Email, ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);

        d.SetBuyer(Buyer.Name, Buyer.PostalCode, Buyer.City, Buyer.Street, CountryCodes.DE, spec.CustomerNumber);
        d.SetBuyerElectronicAddress(Buyer.Email, ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);

        d.SetPaymentMeans(PaymentMeansTypeCodes.SEPACreditTransfer, "Überweisung");
        d.AddCreditorFinancialAccount(Bank.Iban.Replace(" ", ""), Bank.Bic, bankName: Bank.Name);
        d.AddTradePaymentTerms($"Zahlbar bis {spec.DueDate:dd.MM.yyyy} ohne Abzug", spec.DueDate.ToDateTime(TimeOnly.MinValue));

        foreach (var line in calc.Lines)
        {
            var item = d.AddTradeLineItem(
                lineID: line.Number.ToString(),
                name: line.Line.Name,
                netUnitPrice: line.Line.Price,
                unitCode: line.Line.UnitCode,
                grossUnitPrice: line.Line.Price,
                billedQuantity: line.Line.Quantity,
                lineTotalAmount: line.Net,
                taxType: TaxTypes.VAT,
                categoryCode: TaxCategoryCodes.S,
                taxPercent: line.Line.VatRate,
                sellerAssignedID: line.Line.ArticleNumber);

            if (line.Discount != 0)
            {
                item.AddSpecifiedTradeAllowance(
                    currency: CurrencyCodes.EUR,
                    basisAmount: line.Gross,
                    actualAmount: line.Discount,
                    chargePercentage: line.Line.DiscountPercent,
                    reason: "Rabatt");
            }
        }

        if (calc.Allowance != 0)
        {
            d.AddTradeAllowance(
                basisAmount: calc.LineTotal,
                currency: CurrencyCodes.EUR,
                actualAmount: calc.Allowance,
                chargePercentage: spec.AllowancePercent,
                reason: spec.AllowanceReason ?? "Nachlass",
                taxTypeCode: TaxTypes.VAT,
                taxCategoryCode: TaxCategoryCodes.S,
                taxPercent: calc.Taxes[0].Rate);
        }

        foreach (var tax in calc.Taxes)
            d.AddApplicableTradeTax(tax.Basis, tax.Rate, tax.Tax, TaxTypes.VAT, TaxCategoryCodes.S);

        d.SetTotals(
            lineTotalAmount: calc.LineTotal,
            chargeTotalAmount: 0m,
            allowanceTotalAmount: calc.Allowance,
            taxBasisAmount: calc.Net,
            taxTotalAmount: calc.Tax,
            grandTotalAmount: calc.Gross,
            totalPrepaidAmount: 0m,
            duePayableAmount: calc.Gross);

        return d;
    }
}
