# CLAUDE.md — Invoice Processing (XRechnung, ZUGFeRD & OpenAI)

Windows desktop application (.NET 10, WPF) that imports supplier invoices: e-invoices (XRechnung UBL/CII,
ZUGFeRD/Factur-X) are parsed directly, other PDFs (including scans) are read with the OpenAI API (Structured Outputs).
Every invoice is validated, checked for duplicates and stored with its line items in PostgreSQL.
User-facing documentation: `README.md`; design rationale: `DECISIONS.md` (keep both in sync with the code).

## Repository rules

- **Languages:** code, identifiers, comments, commit messages and documentation in English. Everything the end user
  sees is German: UI texts, validation and log messages, `.fehler.txt` reports, the German DB views.
- **No secrets in the repository.** The OpenAI key comes from user secrets (id `invoice-processing`), the environment
  variable `OPENAI_API_KEY` or `OpenAI:ApiKey` at deployment. Never print or log a key. The local database password in
  `docker-compose.yml` / `appsettings.Development.json` is for the local container only.
- **Runtime dependencies stay open source** (MIT, Apache 2.0, PostgreSQL License). No AGPL libraries (iText), no paid
  OCR or document services. QuestPDF only in `tools/TestInvoiceGenerator`.
- **Test data stays fictitious:** VAT IDs must fail the German check digit (ISO 7064 MOD 11,10), e-mail/web use the
  `.example` domain, phone numbers the film/TV range `+49 89 99998 xxx`, IBAN the documentation example. Regenerate
  `testdata/` with the generator instead of editing files by hand.
- Don't tune the extraction prompt to the test invoices; prompt rules must hold for real invoices.
- Ask before committing or pushing.

## Structure

```
src/InvoiceProcessing.Core            Model/ (Invoice, InvoiceLine, Party), Reading/ (IInvoiceReader, InvoiceDocument,
                                      exceptions), Validation/ (InvoiceValidator), Processing/ (InvoiceProcessor = per-file
                                      flow, repository/query/archive interfaces, InvoiceKeyFactory, ProcessingOutcome)
src/InvoiceProcessing.Infrastructure  Readers/ (XmlInvoiceReader, ZugferdPdfReader, OpenAiPdfReader, ZugferdMapper,
                                      InvoiceExtractionSchema = prompt + JSON schema), Persistence/ (PostgresInvoiceRepository,
                                      DatabaseInitializer, schema.sql embedded), Import/ (InvoiceImportService,
                                      FileDocumentArchive, StorageOptions), ServiceCollectionExtensions (reader order = priority)
src/InvoiceProcessing.App             WPF + CommunityToolkit.Mvvm: MainWindow (tabs Import / Rechnungen / Protokoll),
                                      ViewModels/, Converters/; Generic Host + Serilog; exe name Rechnungsverarbeitung
tests/InvoiceProcessing.Tests         xUnit: validator, keys, readers; integration tests (Category=Integration) against
                                      PostgreSQL in their own database rechnungen_test
tools/TestInvoiceGenerator            writes the 7 test invoices to testdata/
tools/ExtractionCheck                 reads files without a database and prints extraction + validation
scripts/reset-local.ps1               start PostgreSQL, empty tables and data\ folders (-FillInbox copies test invoices)
scripts/ui-automation.ps1             drive and screenshot the app without a human (see below)
docs/images/                          README screenshots
```

## Commands

```
docker compose up -d                                    # PostgreSQL 17 (container rechnungen-db, localhost:5432)
dotnet run --project src/InvoiceProcessing.App          # Development env (launchSettings): local DB, folders under data\
dotnet build                                            # stop a running app first: Stop-Process -Name Rechnungsverarbeitung
dotnet test                                             # 25 tests; integration tests need the database
dotnet test --filter "Category!=Integration"
dotnet run --project tools/TestInvoiceGenerator         # regenerate testdata/
dotnet run --project tools/ExtractionCheck -- testdata  # env overrides: OpenAI__Model, OpenAI__ReasoningEffort
dotnet publish src/InvoiceProcessing.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

- Local DB: `Host=localhost;Port=5432;Database=rechnungen;Username=rechnungen;Password=rechnungen_local`.
  Quick look: `docker exec rechnungen-db psql -U rechnungen -d rechnungen -c "select * from v_rechnungen"`.
- `appsettings.Development.json` paths are relative to `bin\Debug\net10.0-windows` (`../../../../../data/...`).
- CI (`.github/workflows/ci.yml`): Windows job builds everything and runs unit tests; Linux job runs all tests against
  a PostgreSQL service container. The test project only references Core and Infrastructure, so it runs on Linux.

## Expected results (regression check)

All 7 test invoices via "Eingangsordner verarbeiten" must end with
**"7 Datei(en) verarbeitet: 5 importiert, 1 Duplikat(e), 1 mit Fehler."**

| File | Path | Expected |
|---|---|---|
| 01 Kaltenbrunn .xml | XRechnung UBL | imported, 4 lines, gross 878,70 |
| 02 Hofstetter .xml | XRechnung CII | imported, line discounts 10 % / 5 %, allowance 14,64, net 717,22, gross 853,49 |
| 03 Lindmayr .pdf | ZUGFeRD (factur-x.xml, profile Comfort) | imported, 7 % VAT, gross 582,42 |
| 04 Brennwald .pdf | OpenAI | imported, free quantities 4/2/2, discounts 5/3/10 %, 19 % + 7 % VAT, gross 1.268,82 |
| 05 Seidl Scan .pdf | OpenAI (image only, receipt stamp) | imported, bonus line = quantity 0 + free 50, gross 709,24 |
| 06 Hubertus .pdf | OpenAI | rejected after a second read: "Summe der Positionen 456,70 ≠ Nettobetrag 465,70." |
| 07 Kaltenbrunn .pdf | OpenAI | duplicate of 01 |

An AI-read invoice that fails validation is read once more before it is rejected (ADR-010): 04 was once misread
(VAT 184,27 instead of 185,27); the second read fixes such single misreads.

Known limit: the tiny sender line on the scan (05) is unreadable at the model's page resolution; the model returns
"Industriestr. 9" instead of "Industriering 9". Accepted and documented, not fixed by prompt tuning.

## UI checks without a human

```
. .\scripts\ui-automation.ps1
Start-App                                   # Debug exe, Development env, maximized
Set-AppSize 1600 1000                       # optional: fixed size in logical pixels (README screenshots use this)
Invoke-ByName "Eingangsordner verarbeiten"; Start-Sleep 40
Select-ListItem 3; Save-Shot "detail"       # returns the PNG path under artifacts\screenshots; read it to check the UI
Select-ByName "Rechnungen"; Save-Shot "invoices"
Stop-App
```

Buttons carry `AutomationProperties.Name` for this. README screenshots are cropped above the status bar (it shows
local paths): crop box `(8, 0, width - 8, 961)` at 1600×1000.

## Gotchas

- Shell heredocs through the tool layer can turn `\\` into `\`; write JSON/config with the file tools.
- WPF projects don't get `System.IO` in implicit usings.
- OpenAI .NET SDK: `ChatMessageContentPart.CreateFilePart` and `ReasoningEffortLevel` are experimental (OPENAI001,
  pragma in `OpenAiPdfReader.cs`); WPF Fluent theme warning WPF0001 is suppressed in the app csproj.
- NuGet id is `PdfPig` (not `UglyToad.PdfPig`).
- Dapper passes `DateOnly` parameters through to Npgsql; reading `date` returns `DateTime`, so read models use
  `DateTime`. `timestamptz` comes back as UTC (`LocalTimeConverter` in the UI).
- XRechnung output from ZUGFeRD-csharp requires a seller contact phone (BR-DE-6).
- Screenshots: `CopyFromScreen` captures whatever is on top; `ui-automation.ps1` uses `PrintWindow`.
- Windows PowerShell 5 needs a UTF-8 BOM in `.ps1` files that contain umlauts.
