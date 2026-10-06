namespace InvoiceProcessing.Core.Model;

/// <summary>
/// Invoice in the internal format. Every reader (XRechnung, ZUGFeRD, PDF via OpenAI) produces this,
/// so validation and storage do not depend on where the data came from.
/// </summary>
public sealed record Invoice
{
    public required string InvoiceNumber { get; init; }
    public required DateOnly InvoiceDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public required string Currency { get; init; }
    public required Party Supplier { get; init; }
    public string? OrderReference { get; init; }

    /// <summary>Document-level allowances (e.g. a loyalty discount on the whole invoice), not line discounts.</summary>
    public decimal AllowanceTotal { get; init; }

    /// <summary>Document-level charges (e.g. freight).</summary>
    public decimal ChargeTotal { get; init; }

    public required decimal NetTotal { get; init; }
    public required decimal TaxTotal { get; init; }
    public required decimal GrossTotal { get; init; }
    public required IReadOnlyList<InvoiceLine> Lines { get; init; }
}

public sealed record Party(string Name, string? VatId, string? Street, string? PostalCode, string? City);

public sealed record InvoiceLine
{
    public required int LineNumber { get; init; }
    public string? ArticleNumber { get; init; }
    public required string Description { get; init; }

    /// <summary>Billed (charged) quantity.</summary>
    public required decimal Quantity { get; init; }

    /// <summary>Free goods / bonus quantity delivered at no charge (Gratis-/Bonusmenge).</summary>
    public decimal FreeQuantity { get; init; }

    public string? Unit { get; init; }

    /// <summary>Net unit price before the line discount.</summary>
    public required decimal UnitPrice { get; init; }

    public decimal? DiscountPercent { get; init; }
    public decimal? DiscountAmount { get; init; }
    public required decimal NetAmount { get; init; }
    public decimal? VatRate { get; init; }
}
