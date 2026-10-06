-- Default data model. To use an existing database model instead, adapt this script and the SQL in
-- PostgresInvoiceRepository. The application runs this script at startup when Database:CreateSchemaIfMissing = true
-- (idempotent).

create table if not exists invoices (
    id                   bigint generated always as identity primary key,
    supplier_name        text          not null,
    supplier_vat_id      text,
    supplier_street      text,
    supplier_postal_code text,
    supplier_city        text,
    supplier_key         text          not null,  -- VAT ID or normalized name (duplicate detection)
    invoice_number       text          not null,
    invoice_number_key   text          not null,  -- normalized invoice number (duplicate detection)
    invoice_date         date          not null,
    due_date             date,
    currency             char(3)       not null,
    order_reference      text,
    allowance_total      numeric(18,2) not null default 0,
    charge_total         numeric(18,2) not null default 0,
    net_total            numeric(18,2) not null,
    tax_total            numeric(18,2) not null,
    gross_total          numeric(18,2) not null,
    source_format        text          not null,  -- XRechnung UBL / XRechnung CII / ZUGFeRD PDF / PDF via OpenAI
    source_file          text          not null,
    archive_path         text,                    -- original file, relative to the archive folder
    file_sha256          char(64)      not null,
    imported_at          timestamptz   not null default now(),
    constraint uq_invoices_file_sha256 unique (file_sha256),
    constraint uq_invoices_supplier_number unique (supplier_key, invoice_number_key)
);

create table if not exists invoice_lines (
    id               bigint generated always as identity primary key,
    invoice_id       bigint        not null references invoices (id) on delete cascade,
    line_number      int           not null,
    article_number   text,
    description      text          not null,
    quantity         numeric(18,4) not null,             -- billed quantity
    free_quantity    numeric(18,4) not null default 0,   -- free goods / bonus quantity
    unit             text,
    unit_price       numeric(18,4) not null,             -- net unit price before the line discount
    discount_percent numeric(9,4),
    discount_amount  numeric(18,2),
    net_amount       numeric(18,2) not null,
    vat_rate         numeric(5,2)
);

create index if not exists ix_invoice_lines_invoice_id on invoice_lines (invoice_id);

create table if not exists processing_log (
    id            bigint generated always as identity primary key,
    processed_at  timestamptz not null default now(),
    file_name     text        not null,
    file_sha256   char(64)    not null,
    status        text        not null check (status in ('imported', 'duplicate', 'validation_failed', 'failed')),
    source_format text,
    invoice_id    bigint references invoices (id),
    message       text        not null,
    errors        text,
    duration_ms   int         not null
);

-- Views with German column names for quick checks by accounting users (pgAdmin, DBeaver, psql)

create or replace view v_rechnungen as
select i.id,
       i.supplier_name  as lieferant,
       i.invoice_number as rechnungsnr,
       i.invoice_date   as datum,
       i.net_total      as netto,
       i.tax_total      as ust,
       i.gross_total    as brutto,
       (select count(*) from invoice_lines l where l.invoice_id = i.id) as positionen,
       i.source_format  as quelle
from invoices i
order by i.id;

create or replace view v_positionen as
select i.invoice_number   as rechnungsnr,
       l.line_number      as pos,
       l.article_number   as artikelnr,
       l.description      as bezeichnung,
       l.quantity         as menge,
       l.free_quantity    as gratis,
       l.unit             as einheit,
       l.unit_price       as einzelpreis,
       l.discount_percent as rabatt_proz,
       l.discount_amount  as rabatt,
       l.net_amount       as betrag,
       l.vat_rate         as ust_satz
from invoice_lines l
join invoices i on i.id = l.invoice_id
order by l.invoice_id, l.line_number;

create or replace view v_protokoll as
select p.processed_at::timestamp(0) as zeit,
       p.file_name                  as datei,
       p.status,
       p.source_format              as quelle,
       p.invoice_id                 as rechnung_id,
       p.message                    as meldung,
       p.errors                     as fehler
from processing_log p
order by p.id;
