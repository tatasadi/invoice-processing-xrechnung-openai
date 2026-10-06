# Architectural Decision Records — Invoice Processing

Each ADR records a significant technology or design choice, the context that drove it, the alternatives that were
considered and the consequences.

---

## ADR-001 — Lean WPF desktop application with an on-demand inbox import

**Status**: Accepted
**Date**: 2026-10

### Context

The target users are accounting staff on Windows machines. The requirement was a lean, reliable Windows application
without an extensive user interface, with invoices provided in a defined inbox folder.

### Decision

A WPF application on .NET 10 with three views (Import, Rechnungen, Protokoll). Invoices are passed by drag & drop or
file dialog, or the defined inbox folder is processed with one click. Hosting, configuration, logging and dependency
injection use the .NET Generic Host, the same way as in a server application.

### Alternatives Considered

| Option | Verdict |
|---|---|
| Windows service watching the inbox folder | No visible feedback for the user; extracted data and rejection reasons are only in logs. Remains possible: the import logic lives in `InvoiceImportService`, not in the UI |
| Console application + Task Scheduler | Same feedback problem; awkward for non-technical users |
| Web application | Needs hosting and authentication; not justified for a single-site desktop workflow |
| WinUI 3 / .NET MAUI | Packaging and deployment overhead; WPF ships with .NET and has a built-in Fluent theme since .NET 9 |

### Consequences

The user sees every result immediately: what was read, why an invoice was rejected, which file was a duplicate.
The UI project is Windows-only; Core and Infrastructure stay cross-platform (CI runs their tests on Linux).

---

## ADR-002 — Structured e-invoices are parsed deterministically; AI is only the fallback

**Status**: Accepted
**Date**: 2026-10

### Context

XRechnung and ZUGFeRD carry the complete invoice data as XML (EN 16931). Sending them to a language model would add
cost, latency, a data-protection question and a source of errors for data that is already exact.

### Decision

Readers are asked in a fixed order: `XmlInvoiceReader` (XRechnung UBL/CII), `ZugferdPdfReader` (PDF with embedded
XML), and only then `OpenAiPdfReader`. Parsing uses the open-source library ZUGFeRD-csharp; PdfPig finds the embedded
XML.

### Consequences

E-invoices are processed locally in milliseconds and without AI risk. The share of invoices that needs AI shrinks
as more suppliers send e-invoices.

---

## ADR-003 — OpenAI Structured Outputs with direct PDF input

**Status**: Accepted
**Date**: 2026-10

### Context

PDF invoices vary in layout, contain line items with discounts and free goods, and may be scans without a text
layer. The result must always fit the internal data model.

### Decision

The PDF is sent as file input to the Chat Completions API. The response format is a strict JSON schema
(`InvoiceExtractionSchema`), so every answer has exactly the expected fields. The prompt demands values as printed
(no calculation, no correction), and requests are sent with `store = false`.

### Alternatives Considered

| Option | Verdict |
|---|---|
| Text extraction (PdfPig) + prompt | Fails on scans; loses the table layout |
| Classic OCR + templates per supplier | High maintenance for every new supplier |
| Azure AI Document Intelligence (prebuilt invoice) | Paid additional service and a proprietary dependency; line items with free goods need extra mapping |

### Consequences

One code path for digital PDFs and scans. Values "as printed" make the validation (ADR-005) meaningful: a printed
error is detected instead of silently corrected by the model.

---

## ADR-004 — Model: gpt-5.4-mini

**Status**: Accepted
**Date**: 2026-10

### Context

Invoice extraction needs accurate reading of tables, not long reasoning. Cost and latency matter because the user
waits for the result.

### Decision

`gpt-5.4-mini` with reasoning effort `low`, configurable via `OpenAI:Model`.

### Measurement (the four PDF test invoices)

| Model | Correct extractions | Time per invoice | Tokens per invoice |
|---|---|---|---|
| gpt-5-mini | 4 of 4 | 7–13 s | ~2,800–3,200 |
| gpt-5.4-mini | 4 of 4 | 2.6–7.5 s | ~2,500–3,100 |

Both models read the tiny sender line of the scanned invoice incorrectly (see README, limits); all amounts, numbers
and VAT IDs were correct. `gpt-5.4-mini` also filled the VAT rate for invoices without a VAT column.

### Consequences

Fast enough for interactive use; the model can be changed without code changes.

---

## ADR-005 — Validation gate before storage

**Status**: Accepted
**Date**: 2026-10

### Context

AI extraction can be wrong, and printed invoices can contain errors. Accounting data must not be stored on trust.

### Decision

`InvoiceValidator` checks every invoice, regardless of the source: required fields, plausible date, quantity × price
− discount per line, sum of lines − allowances + charges = net total, net + VAT = gross, VAT per rate. Any failure
means the invoice is not stored; the reasons are shown and logged.

### Consequences

Arithmetic checks catch both extraction errors and genuinely wrong invoices (test invoice 06). A tolerance
(`Validation:AmountTolerance`) absorbs rounding differences.

---

## ADR-006 — Duplicate detection on three levels

**Status**: Accepted
**Date**: 2026-10

### Context

The same invoice often arrives twice: the same file again, or once as e-invoice and once as PDF by e-mail.

### Decision

1. SHA-256 of the file (identical file, checked before any AI call).
2. Supplier key + invoice number key: the supplier key is the normalized VAT ID (name as fallback), the invoice number
   is normalized to letters and digits, so `RE-2026-10471` and `RE 2026 10471` match.
3. Fallback when the supplier key differs between sources: same invoice number + date + gross amount.

Levels 1 and 2 are also unique constraints in PostgreSQL, which protects against concurrent imports.

### Consequences

Test invoice 07 (the PDF of the XRechnung 01) is detected, although it is a different file read by a different reader.

---

## ADR-007 — Npgsql + Dapper with plain SQL instead of an ORM

**Status**: Accepted
**Date**: 2026-10

### Context

In practice the invoices must often go into an existing database with a given data model.

### Decision

`PostgresInvoiceRepository` uses Npgsql and Dapper with explicit SQL. The schema is an embedded, idempotent script
that the application runs at startup if configured (`Database:CreateSchemaIfMissing`).

### Alternatives Considered

| Option | Verdict |
|---|---|
| Entity Framework Core with migrations | Strong for a database the application owns; adapting to a foreign schema means fighting conventions and migrations |

### Consequences

Mapping to another data model is a change in one class and one script. Header and lines are written in one
transaction.

---

## ADR-008 — Transient failures are separated from rejected invoices

**Status**: Accepted
**Date**: 2026-10

### Context

If the database or the OpenAI API is unavailable, or the API key is missing, the invoice itself is fine. Moving it to
the error folder would mislead the user.

### Decision

Readers and the repository raise `TransientProcessingException` for such cases. The result is the status "Nicht
verarbeitet": the file stays in the inbox, nothing is written to the error folder or the processing log, and the
import can simply be repeated. `InvoiceReadException` marks files that are genuinely unreadable.

### Consequences

The error folder only contains invoices that need human attention.

---

## ADR-009 — Open-source runtime dependencies only

**Status**: Accepted
**Date**: 2026-10

### Context

The operator should not depend on paid third-party services or proprietary libraries beyond the OpenAI API itself.

### Decision

Runtime dependencies are MIT, Apache 2.0 or PostgreSQL licensed: ZUGFeRD-csharp, PdfPig, Npgsql, Dapper, OpenAI .NET
SDK, Serilog, CommunityToolkit.Mvvm, Microsoft.Extensions.*. AGPL libraries (e.g. iText) and paid OCR services are
excluded. QuestPDF (community license) is used only by the test invoice generator.

### Consequences

The application can be extended and redistributed by the operator without licensing questions.
