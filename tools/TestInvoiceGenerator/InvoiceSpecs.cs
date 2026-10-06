using s2industries.ZUGFeRD;

namespace TestInvoiceGenerator;

// All companies and people are fictitious. VAT IDs fail the official check digit on purpose, e-mail and web
// addresses use the reserved .example domain, phone numbers the range reserved for film and TV (089 99998 xxx),
// and the IBAN is the common documentation example.

internal sealed record Company(
    string Name,
    string Tagline,
    string Street,
    string PostalCode,
    string City,
    string VatId,
    string Phone,
    string Email,
    string Web,
    string ManagingDirector,
    string Accent);

internal sealed record LineSpec(
    string ArticleNumber,
    string Name,
    decimal Quantity,
    string Unit,
    QuantityCodes UnitCode,
    decimal Price,
    decimal DiscountPercent = 0,
    decimal FreeQuantity = 0,
    decimal VatRate = 19);

internal sealed record InvoiceSpec(
    string FileName,
    Company Supplier,
    string Number,
    DateOnly Date,
    string CustomerNumber,
    string OrderNumber,
    LineSpec[] Lines,
    int PaymentDays = 30,
    decimal AllowancePercent = 0,
    string? AllowanceReason = null,
    decimal? PrintedNetOverride = null)
{
    public DateOnly DueDate => Date.AddDays(PaymentDays);
}

internal static class Buyer
{
    public const string Name = "Beispiel Großhandel GmbH";
    public const string Department = "Rechnungseingang";
    public const string Street = "Gewerbestraße 14";
    public const string PostalCode = "80331";
    public const string City = "München";
    public const string Email = "rechnungen@beispiel-grosshandel.example";
}

internal static class Bank
{
    public const string Name = "Musterbank eG";
    public const string Iban = "DE89 3704 0044 0532 0130 00";
    public const string Bic = "COBADEFFXXX";
}

internal static class InvoiceSpecs
{
    private static readonly Company Kaltenbrunn = new(
        "Kaltenbrunn Bürobedarf GmbH", "Büro- und Geschäftsbedarf", "Leopoldstraße 50", "80802", "München",
        "DE811234567", "+49 89 99998 101", "buchhaltung@kaltenbrunn-buero.example", "www.kaltenbrunn-buero.example", "Martina Kaltenbrunn", "#1F4E79");

    private static readonly Company Hofstetter = new(
        "Hofstetter Werkzeug KG", "Werkzeuge · Maschinen · Arbeitsschutz", "Am Werkhof 7", "90402", "Nürnberg",
        "DE298765432", "+49 89 99998 102", "rechnung@hofstetter-werkzeug.example", "www.hofstetter-werkzeug.example", "Josef Hofstetter", "#B23A1E");

    private static readonly Company Lindmayr = new(
        "Lindmayr Feinkost GmbH", "Feinkost für Handel und Gastronomie", "Marktplatz 3", "94032", "Passau",
        "DE145678904", "+49 89 99998 103", "faktura@lindmayr-feinkost.example", "www.lindmayr-feinkost.example", "Anna Lindmayr", "#2E6B3A");

    private static readonly Company Brennwald = new(
        "Brennwald Getränke GmbH", "Getränkefachgroßhandel", "Brunnenweg 21", "93047", "Regensburg",
        "DE267890124", "+49 89 99998 104", "rechnungen@brennwald-getraenke.example", "www.brennwald-getraenke.example", "Thomas Brennwald", "#0F6E8C");

    private static readonly Company Seidl = new(
        "Seidl Verpackung GmbH", "Verpackung · Versand · Lager", "Industriering 9", "86154", "Augsburg",
        "DE189012345", "+49 89 99998 105", "buchhaltung@seidl-verpackung.example", "www.seidl-verpackung.example", "Peter Seidl", "#6B4E9B");

    private static readonly Company Hubertus = new(
        "Hubertus Elektro GmbH", "Elektrogroßhandel", "Siemensstraße 18", "84030", "Landshut",
        "DE231456789", "+49 89 99998 106", "rechnung@hubertus-elektro.example", "www.hubertus-elektro.example", "Hubert Mair", "#C07A00");

    public static readonly InvoiceSpec KaltenbrunnInvoice = new(
        "01_Kaltenbrunn_RE-2026-10471", Kaltenbrunn, "RE-2026-10471", new DateOnly(2026, 9, 28), "K-40217", "EK-2026-0815",
        [
            new("KP-A4-80", "Kopierpapier A4, 80 g/m², 500 Blatt", 10, "Pack", QuantityCodes.XPK, 4.29m),
            new("TN-26A-BK", "Tonerkartusche schwarz, kompatibel", 5, "Stk", QuantityCodes.H87, 54.90m),
            new("OR-A4-80-BL", "Ordner A4, 80 mm, blau", 20, "Stk", QuantityCodes.H87, 2.15m),
            new("BS-ERGO-200", "Bürostuhl ergonomisch, Netzrücken", 2, "Stk", QuantityCodes.H87, 189.00m),
        ]);

    public static readonly InvoiceSpec HofstetterInvoice = new(
        "02_Hofstetter_2026-4418", Hofstetter, "2026-4418", new DateOnly(2026, 9, 30), "10-5582", "EK-2026-0821",
        [
            new("AB-18V-2A", "Akku-Bohrschrauber 18 V, 2 Akkus", 4, "Stk", QuantityCodes.H87, 129.00m, DiscountPercent: 10),
            new("BS-32", "Bit-Satz 32-teilig", 10, "Stk", QuantityCodes.H87, 12.50m),
            new("SB-166-K", "Schutzbrille klar, EN 166", 6, "Stk", QuantityCodes.H87, 7.80m, DiscountPercent: 5),
            new("WK-500", "Werkzeugkoffer leer, 5 Fächer", 2, "Stk", QuantityCodes.H87, 49.00m),
        ],
        AllowancePercent: 2, AllowanceReason: "Treuerabatt");

    public static readonly InvoiceSpec LindmayrInvoice = new(
        "03_Lindmayr_LF-26-03391", Lindmayr, "LF-26-03391", new DateOnly(2026, 10, 1), "KD-7731", "EK-2026-0830",
        [
            new("BK-12-1000", "Bergkäse 12 Monate gereift, 1 kg", 12, "Stk", QuantityCodes.H87, 18.90m, VatRate: 7),
            new("OO-075", "Olivenöl nativ extra, 0,75 l", 24, "Fl", QuantityCodes.H87, 8.95m, VatRate: 7),
            new("NB-500", "Nudeln Bronze, 500 g", 30, "Pack", QuantityCodes.XPK, 2.35m, VatRate: 7),
            new("TP-690", "Tomaten passiert, 690 g", 18, "Glas", QuantityCodes.H87, 1.79m, VatRate: 7),
        ],
        PaymentDays: 14);

    public static readonly InvoiceSpec BrennwaldInvoice = new(
        "04_Brennwald_BG-2026-10-0157", Brennwald, "BG-2026-10-0157", new DateOnly(2026, 10, 2), "20931", "EK-2026-0834",
        [
            new("10215", "Mineralwasser classic, 12 × 1,0 l", 40, "Kiste", QuantityCodes.XCS, 6.49m, FreeQuantity: 4),
            new("10230", "Apfelschorle, 12 × 1,0 l", 25, "Kiste", QuantityCodes.XCS, 8.99m, DiscountPercent: 5, FreeQuantity: 2),
            new("20410", "Orangensaft 100 %, 6 × 1,0 l", 15, "Kiste", QuantityCodes.XCS, 11.40m),
            new("30120", "Cola, 24 × 0,33 l Dose", 20, "Karton", QuantityCodes.XBX, 13.80m, DiscountPercent: 3, FreeQuantity: 2),
            new("40005", "Kaffeebohnen Crema, 1 kg", 12, "Pack", QuantityCodes.XPK, 15.90m, DiscountPercent: 10, VatRate: 7),
        ],
        PaymentDays: 14);

    public static readonly InvoiceSpec SeidlInvoice = new(
        "05_Seidl_Scan_SV-88213", Seidl, "SV-88213", new DateOnly(2026, 9, 25), "3304", "EK-2026-0809",
        [
            new("VK-302015", "Versandkarton 300 × 200 × 150 mm, 2-wellig", 500, "Stk", QuantityCodes.H87, 0.62m),
            new("PB-5066-BR", "Packband braun, 50 mm × 66 m", 20, "Rolle", QuantityCodes.XRO, 1.85m),
            new("LP-10050", "Luftpolsterfolie, 100 cm × 50 m", 10, "Rolle", QuantityCodes.XRO, 24.90m),
            new("VK-302015", "Versandkarton 300 × 200 × 150 mm – Bonusmenge", 50, "Stk", QuantityCodes.H87, 0m),
        ]);

    /// <summary>Printed net total contains a typo (465,70 instead of 456,70): validation must reject it.</summary>
    public static readonly InvoiceSpec HubertusInvoice = new(
        "06_Hubertus_HE-2026-0912", Hubertus, "HE-2026-0912", new DateOnly(2026, 10, 3), "55018", "EK-2026-0838",
        [
            new("LR-T8-120", "LED-Röhre T8, 120 cm, 18 W", 50, "Stk", QuantityCodes.H87, 6.90m),
            new("KB-200", "Kabelbinder schwarz, 200 mm (100 Stk)", 10, "Pack", QuantityCodes.XPK, 3.20m),
            new("VK-5M-3", "Verlängerungskabel 5 m, 3-fach", 5, "Stk", QuantityCodes.H87, 15.94m),
        ],
        PrintedNetOverride: 465.70m);

    /// <summary>The same invoice as no. 01, but as a plain PDF (e.g. sent again by e-mail): must be detected as duplicate.</summary>
    public static readonly InvoiceSpec KaltenbrunnPdfCopy = KaltenbrunnInvoice with { FileName = "07_Kaltenbrunn_RE-2026-10471" };
}
