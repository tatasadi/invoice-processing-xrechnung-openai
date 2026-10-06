using InvoiceProcessing.Core.Model;

namespace InvoiceProcessing.Core.Processing;

/// <summary>
/// Normalized keys for duplicate detection. The same invoice can arrive as XRechnung and as PDF, read once
/// without and once with AI, so spaces, dots and case must not make two copies look different.
/// </summary>
public static class InvoiceKeyFactory
{
    public static InvoiceKeys Create(Invoice invoice) => new(SupplierKey(invoice.Supplier), InvoiceNumberKey(invoice.InvoiceNumber));

    /// <summary>VAT ID if present (most reliable), otherwise the normalized supplier name.</summary>
    public static string SupplierKey(Party supplier)
    {
        var vatId = AlphaNumericUpper(supplier.VatId);
        return vatId.Length >= 4 ? "VAT:" + vatId : "NAME:" + AlphaNumericUpper(supplier.Name);
    }

    public static string InvoiceNumberKey(string invoiceNumber) => AlphaNumericUpper(invoiceNumber);

    private static string AlphaNumericUpper(string? value) =>
        value is null ? "" : string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
