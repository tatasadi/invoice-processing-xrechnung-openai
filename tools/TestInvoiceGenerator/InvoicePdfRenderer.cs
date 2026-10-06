using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TestInvoiceGenerator;

/// <summary>Renders a spec as a realistic German supplier invoice (visual PDF only, no e-invoice data).</summary>
internal static class InvoicePdfRenderer
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private const string Ink = "#222222";
    private const string Muted = "#6B6B6B";
    private const string Rule = "#D0D0D0";

    public static Document Create(InvoiceSpec spec, string? receivedStamp = null, string? eInvoiceNote = null)
    {
        var calc = InvoiceCalculation.For(spec);
        var supplier = spec.Supplier;

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(48);
            page.MarginVertical(36);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Ink));

            page.Header().Column(header =>
            {
                header.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(supplier.Name).FontSize(17).Bold().FontColor(supplier.Accent);
                        c.Item().Text(supplier.Tagline).FontSize(9).FontColor(Muted);
                    });
                    row.ConstantItem(160).AlignRight().AlignBottom().Text("RECHNUNG").FontSize(18).SemiBold().FontColor("#9A9A9A").LetterSpacing(0.08f);
                });
                header.Item().PaddingTop(8).LineHorizontal(2).LineColor(supplier.Accent);
            });

            page.Content().PaddingTop(22).Column(content =>
            {
                content.Item().Row(row =>
                {
                    row.RelativeItem(3).Column(address =>
                    {
                        address.Item().Text($"{supplier.Name} · {supplier.Street} · {supplier.PostalCode} {supplier.City}")
                            .FontSize(6.5f).FontColor(Muted).Underline();
                        address.Item().PaddingTop(6).Text(Buyer.Name).FontSize(10);
                        address.Item().Text(Buyer.Department).FontSize(10);
                        address.Item().Text(Buyer.Street).FontSize(10);
                        address.Item().Text($"{Buyer.PostalCode} {Buyer.City}").FontSize(10);
                    });

                    row.RelativeItem(2).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        void Info(string label, string value)
                        {
                            table.Cell().PaddingVertical(1.5f).Text(label).FontColor(Muted);
                            table.Cell().PaddingVertical(1.5f).AlignRight().Text(value).SemiBold();
                        }

                        Info("Rechnungsnummer", spec.Number);
                        Info("Rechnungsdatum", spec.Date.ToString("dd.MM.yyyy"));
                        Info("Lieferdatum", spec.Date.ToString("dd.MM.yyyy"));
                        Info("Kundennummer", spec.CustomerNumber);
                        Info("Ihre Bestellung", spec.OrderNumber);
                    });
                });

                content.Item().PaddingTop(28).Text($"Rechnung Nr. {spec.Number}").FontSize(13).Bold();
                content.Item().PaddingTop(6).Text("Sehr geehrte Damen und Herren, für unsere Lieferung berechnen wir Ihnen:");

                content.Item().PaddingTop(12).Element(e => LinesTable(e, spec, calc));
                content.Item().PaddingTop(10).AlignRight().Width(250).Element(e => Totals(e, spec, calc));

                content.Item().PaddingTop(22).Text($"Zahlbar bis {spec.DueDate:dd.MM.yyyy} ohne Abzug auf das unten genannte Konto. "
                                                   + $"Bitte geben Sie die Rechnungsnummer {spec.Number} als Verwendungszweck an.");
                if (eInvoiceNote is not null)
                    content.Item().PaddingTop(6).Text(eInvoiceNote).FontSize(8).FontColor(Muted);
                content.Item().PaddingTop(10).Text("Vielen Dank für Ihren Auftrag.");
            });

            page.Footer().Column(footer =>
            {
                footer.Item().LineHorizontal(0.5f).LineColor(Rule);
                footer.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(supplier.Name).FontSize(7).SemiBold();
                        c.Item().Text($"{supplier.Street}, {supplier.PostalCode} {supplier.City}").FontSize(7);
                        c.Item().Text($"Geschäftsführung: {supplier.ManagingDirector}").FontSize(7);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"Telefon {supplier.Phone}").FontSize(7);
                        c.Item().Text(supplier.Email).FontSize(7);
                        c.Item().Text(supplier.Web).FontSize(7);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"{Bank.Name} · BIC {Bank.Bic}").FontSize(7);
                        c.Item().Text($"IBAN {Bank.Iban}").FontSize(7);
                        c.Item().Text($"USt-IdNr. {supplier.VatId}").FontSize(7);
                    });
                });
                footer.Item().PaddingTop(6).AlignCenter().Text("Fiktive Testrechnung – alle Firmen-, Personen- und Bankdaten sind erfunden.")
                    .FontSize(6.5f).FontColor("#9A9A9A");
            });

            if (receivedStamp is not null)
            {
                page.Foreground().AlignRight().AlignTop().PaddingTop(205).PaddingRight(70).Rotate(-7)
                    .Border(1.8f).BorderColor("#C62828").PaddingVertical(4).PaddingHorizontal(10)
                    .Column(stamp =>
                    {
                        stamp.Item().AlignCenter().Text("EINGEGANGEN").FontSize(11).Bold().FontColor("#C62828").LetterSpacing(0.1f);
                        stamp.Item().AlignCenter().Text(receivedStamp).FontSize(10).SemiBold().FontColor("#C62828");
                    });
            }
        }));
    }

    private static void LinesTable(IContainer container, InvoiceSpec spec, InvoiceCalculation calc)
    {
        var showFree = spec.Lines.Any(l => l.FreeQuantity != 0);
        var showDiscount = spec.Lines.Any(l => l.DiscountPercent != 0);
        var showVat = spec.Lines.Select(l => l.VatRate).Distinct().Count() > 1;

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(20);
                c.ConstantColumn(64);
                c.RelativeColumn();
                c.ConstantColumn(34);
                if (showFree) c.ConstantColumn(32);
                c.ConstantColumn(36);
                c.ConstantColumn(54);
                if (showDiscount) c.ConstantColumn(34);
                c.ConstantColumn(54);
                if (showVat) c.ConstantColumn(28);
            });

            table.Header(h =>
            {
                void Head(string text, bool right = false)
                {
                    var cell = h.Cell().Background("#F2F2F2").PaddingVertical(4).PaddingHorizontal(3);
                    (right ? cell.AlignRight() : cell).Text(text).SemiBold().FontSize(8);
                }

                Head("Pos.");
                Head("Art.-Nr.");
                Head("Bezeichnung");
                Head("Menge", true);
                if (showFree) Head("Gratis", true);
                Head("Einheit");
                Head("Einzelpreis €", true);
                if (showDiscount) Head("Rabatt", true);
                Head("Betrag €", true);
                if (showVat) Head("USt", true);
            });

            foreach (var line in calc.Lines)
            {
                void Cell(string text, bool right = false)
                {
                    var cell = table.Cell().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(3);
                    (right ? cell.AlignRight() : cell).Text(text);
                }

                Cell(line.Number.ToString());
                Cell(line.Line.ArticleNumber);
                Cell(line.Line.Name);
                Cell(Quantity(line.Line.Quantity), true);
                if (showFree) Cell(line.Line.FreeQuantity == 0 ? "–" : Quantity(line.Line.FreeQuantity), true);
                Cell(line.Line.Unit);
                Cell(Money(line.Line.Price), true);
                if (showDiscount) Cell(line.Line.DiscountPercent == 0 ? "–" : $"{line.Line.DiscountPercent.ToString("0.##", German)} %", true);
                Cell(Money(line.Net), true);
                if (showVat) Cell($"{line.Line.VatRate:0} %", true);
            }
        });
    }

    private static void Totals(IContainer container, InvoiceSpec spec, InvoiceCalculation calc)
    {
        container.Column(column =>
        {
            void Line(string label, decimal amount, bool bold = false)
            {
                column.Item().PaddingVertical(2).Row(row =>
                {
                    var labelText = row.RelativeItem().Text(label);
                    var amountText = row.ConstantItem(80).AlignRight().Text($"{Money(amount)} €");
                    if (bold)
                    {
                        labelText.Bold();
                        amountText.Bold();
                    }
                });
            }

            if (calc.Allowance != 0)
            {
                Line("Summe Positionen", calc.LineTotal);
                Line($"abzgl. {spec.AllowanceReason} {spec.AllowancePercent:0.##} %", -calc.Allowance);
            }

            Line("Summe netto", calc.Net);
            foreach (var tax in calc.Taxes)
                Line($"zzgl. USt {tax.Rate:0} % auf {Money(tax.Basis)} €", tax.Tax);
            column.Item().PaddingTop(2).LineHorizontal(1).LineColor(Ink);
            Line("Rechnungsbetrag", calc.Gross, bold: true);
        });
    }

    private static string Money(decimal value) => value.ToString("N2", German);

    private static string Quantity(decimal value) => value == decimal.Truncate(value) ? value.ToString("N0", German) : value.ToString("N2", German);
}
