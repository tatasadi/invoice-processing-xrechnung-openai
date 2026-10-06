namespace InvoiceProcessing.Infrastructure.Readers;

/// <summary>
/// Prompt and JSON schema for reading PDF invoices with OpenAI. Structured Outputs (strict mode) guarantee
/// that the answer always matches this schema; the schema is the "defined data format" of the extraction.
/// </summary>
internal static class InvoiceExtractionSchema
{
    public const string Instructions =
        """
        You extract data from German B2B supplier invoices (Eingangsrechnungen) for automated import into an ERP database.

        Rules:
        - Return exactly the values printed on the document. Never calculate, correct or guess values.
          If a value is not printed, return null. If printed totals do not add up, still return them as printed.
        - Copy texts (company names, street names, article numbers, descriptions) character by character as printed;
          do not abbreviate, normalize or correct them.
        - Amounts and quantities are numbers with a dot as decimal separator, without currency symbols or thousands separators.
        - Dates as YYYY-MM-DD. Currency as ISO 4217 code (e.g. EUR).
        - supplier = the company that issued the invoice (Rechnungssteller), never the invoice recipient.
          vat_id = the supplier's VAT ID (USt-IdNr.), not the tax number (Steuernummer).
        - lines: every invoiced item in printed order. line_number = position number as printed, otherwise 1, 2, 3, ...
        - quantity = the billed (charged) quantity.
        - free_quantity = free goods / bonus quantity for that item delivered at no charge (columns like "Gratis", "Bonus",
          "Naturalrabatt", or text like "10 + 2 gratis"); 0 if none. If free goods are printed as a separate line with
          price 0, return that line with quantity 0 and the free amount in free_quantity.
        - unit_price = net unit price before the line discount. discount_percent / discount_amount = the line discount as printed.
        - net_amount = the line total as printed. vat_rate = VAT rate of the line in percent (e.g. 19 or 7); if the lines
          have no VAT column and the invoice shows only one VAT rate, use that rate for every line.
        - allowance_total / charge_total = document-level discounts or surcharges (not line discounts), e.g. a loyalty
          discount or freight; null if none.
        - net_total, tax_total, gross_total = the invoice totals as printed.
        - is_invoice = false if the document is not an invoice (e.g. a delivery note or reminder).
        - Ignore stamps, handwritten notes and booking marks added by the recipient.
        """;

    public const string Json =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["is_invoice", "supplier", "invoice_number", "invoice_date", "due_date", "currency", "order_reference",
                       "allowance_total", "charge_total", "net_total", "tax_total", "gross_total", "lines"],
          "properties": {
            "is_invoice": { "type": "boolean" },
            "supplier": {
              "type": "object",
              "additionalProperties": false,
              "required": ["name", "vat_id", "street", "postal_code", "city"],
              "properties": {
                "name": { "type": "string" },
                "vat_id": { "type": ["string", "null"] },
                "street": { "type": ["string", "null"] },
                "postal_code": { "type": ["string", "null"] },
                "city": { "type": ["string", "null"] }
              }
            },
            "invoice_number": { "type": "string" },
            "invoice_date": { "type": "string", "description": "YYYY-MM-DD" },
            "due_date": { "type": ["string", "null"], "description": "YYYY-MM-DD" },
            "currency": { "type": "string" },
            "order_reference": { "type": ["string", "null"] },
            "allowance_total": { "type": ["number", "null"] },
            "charge_total": { "type": ["number", "null"] },
            "net_total": { "type": "number" },
            "tax_total": { "type": "number" },
            "gross_total": { "type": "number" },
            "lines": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["line_number", "article_number", "description", "quantity", "free_quantity", "unit",
                             "unit_price", "discount_percent", "discount_amount", "net_amount", "vat_rate"],
                "properties": {
                  "line_number": { "type": "integer" },
                  "article_number": { "type": ["string", "null"] },
                  "description": { "type": "string" },
                  "quantity": { "type": "number" },
                  "free_quantity": { "type": "number" },
                  "unit": { "type": ["string", "null"] },
                  "unit_price": { "type": "number" },
                  "discount_percent": { "type": ["number", "null"] },
                  "discount_amount": { "type": ["number", "null"] },
                  "net_amount": { "type": "number" },
                  "vat_rate": { "type": ["number", "null"] }
                }
              }
            }
          }
        }
        """;
}
