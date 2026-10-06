using Dapper;
using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Core.Reading;
using Npgsql;

namespace InvoiceProcessing.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL access with Npgsql + Dapper and plain SQL, so the statements can be adapted directly to an
/// existing database schema (no migrations, no ORM conventions).
/// </summary>
public sealed class PostgresInvoiceRepository(NpgsqlDataSource dataSource) : IInvoiceRepository, IInvoiceQueries
{
    private const string ExistingColumns =
        "id as Id, invoice_number as InvoiceNumber, supplier_name as SupplierName, source_file as SourceFile";

    public Task<ExistingInvoice?> FindByFileHashAsync(string sha256, CancellationToken ct) =>
        RunAsync(connection => connection.QueryFirstOrDefaultAsync<ExistingInvoice>(new CommandDefinition(
            $"select {ExistingColumns} from invoices where file_sha256 = @sha256",
            new { sha256 }, cancellationToken: ct)));

    public Task<ExistingInvoice?> FindDuplicateAsync(Invoice invoice, InvoiceKeys keys, CancellationToken ct) =>
        RunAsync(connection => connection.QueryFirstOrDefaultAsync<ExistingInvoice>(new CommandDefinition(
            $"""
             select {ExistingColumns} from invoices
             where invoice_number_key = @InvoiceNumberKey
               and (supplier_key = @SupplierKey or (invoice_date = @InvoiceDate and gross_total = @GrossTotal))
             order by id
             limit 1
             """,
            new { keys.InvoiceNumberKey, keys.SupplierKey, invoice.InvoiceDate, invoice.GrossTotal },
            cancellationToken: ct)));

    public Task<long> InsertAsync(Invoice invoice, InvoiceSource source, CancellationToken ct) => RunAsync(async connection =>
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                insert into invoices (
                    supplier_name, supplier_vat_id, supplier_street, supplier_postal_code, supplier_city, supplier_key,
                    invoice_number, invoice_number_key, invoice_date, due_date, currency, order_reference,
                    allowance_total, charge_total, net_total, tax_total, gross_total,
                    source_format, source_file, archive_path, file_sha256)
                values (
                    @SupplierName, @SupplierVatId, @SupplierStreet, @SupplierPostalCode, @SupplierCity, @SupplierKey,
                    @InvoiceNumber, @InvoiceNumberKey, @InvoiceDate, @DueDate, @Currency, @OrderReference,
                    @AllowanceTotal, @ChargeTotal, @NetTotal, @TaxTotal, @GrossTotal,
                    @Format, @FileName, @ArchivePath, @Sha256)
                returning id
                """,
                new
                {
                    SupplierName = invoice.Supplier.Name,
                    SupplierVatId = invoice.Supplier.VatId,
                    SupplierStreet = invoice.Supplier.Street,
                    SupplierPostalCode = invoice.Supplier.PostalCode,
                    SupplierCity = invoice.Supplier.City,
                    source.Keys.SupplierKey,
                    invoice.InvoiceNumber,
                    source.Keys.InvoiceNumberKey,
                    invoice.InvoiceDate,
                    invoice.DueDate,
                    invoice.Currency,
                    invoice.OrderReference,
                    invoice.AllowanceTotal,
                    invoice.ChargeTotal,
                    invoice.NetTotal,
                    invoice.TaxTotal,
                    invoice.GrossTotal,
                    source.Format,
                    source.FileName,
                    source.ArchivePath,
                    source.Sha256,
                },
                transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into invoice_lines (
                    invoice_id, line_number, article_number, description, quantity, free_quantity, unit,
                    unit_price, discount_percent, discount_amount, net_amount, vat_rate)
                values (
                    @InvoiceId, @LineNumber, @ArticleNumber, @Description, @Quantity, @FreeQuantity, @Unit,
                    @UnitPrice, @DiscountPercent, @DiscountAmount, @NetAmount, @VatRate)
                """,
                invoice.Lines.Select(l => new
                {
                    InvoiceId = id,
                    l.LineNumber,
                    l.ArticleNumber,
                    l.Description,
                    l.Quantity,
                    l.FreeQuantity,
                    l.Unit,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.DiscountAmount,
                    l.NetAmount,
                    l.VatRate,
                }),
                transaction, cancellationToken: ct));

            await transaction.CommitAsync(ct);
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateInvoiceException($"Rechnung {invoice.InvoiceNumber} ist bereits importiert ({ex.ConstraintName})", ex);
        }
    });

    public Task LogAsync(ProcessingLogEntry entry, CancellationToken ct) => RunAsync(connection => connection.ExecuteAsync(new CommandDefinition(
        """
        insert into processing_log (file_name, file_sha256, status, source_format, invoice_id, message, errors, duration_ms)
        values (@FileName, @Sha256, @Status, @Format, @InvoiceId, @Message, @Errors, @DurationMs)
        """,
        new
        {
            entry.FileName,
            entry.Sha256,
            Status = StatusCode(entry.Status),
            entry.Format,
            entry.InvoiceId,
            entry.Message,
            Errors = entry.Errors.Count > 0 ? string.Join(Environment.NewLine, entry.Errors) : null,
            entry.DurationMs,
        },
        cancellationToken: ct)));

    public async Task<IReadOnlyList<StoredInvoice>> ListInvoicesAsync(int limit, CancellationToken ct) =>
        (await RunAsync(connection => connection.QueryAsync<StoredInvoice>(new CommandDefinition(
            """
            select i.id, i.supplier_name as SupplierName, i.supplier_vat_id as SupplierVatId, i.invoice_number as InvoiceNumber,
                   i.invoice_date as InvoiceDate, i.due_date as DueDate, i.currency, i.net_total as NetTotal,
                   i.tax_total as TaxTotal, i.gross_total as GrossTotal,
                   (select count(*) from invoice_lines l where l.invoice_id = i.id) as LineCount,
                   i.source_format as SourceFormat, i.source_file as SourceFile, i.archive_path as ArchivePath,
                   i.imported_at as ImportedAt
            from invoices i
            order by i.id desc
            limit @limit
            """,
            new { limit }, cancellationToken: ct)))).AsList();

    public async Task<IReadOnlyList<InvoiceLine>> ListLinesAsync(long invoiceId, CancellationToken ct) =>
        (await RunAsync(connection => connection.QueryAsync<InvoiceLine>(new CommandDefinition(
            """
            select line_number as LineNumber, article_number as ArticleNumber, description, quantity,
                   free_quantity as FreeQuantity, unit, unit_price as UnitPrice, discount_percent as DiscountPercent,
                   discount_amount as DiscountAmount, net_amount as NetAmount, vat_rate as VatRate
            from invoice_lines
            where invoice_id = @invoiceId
            order by line_number, id
            """,
            new { invoiceId }, cancellationToken: ct)))).AsList();

    public async Task<IReadOnlyList<ProcessingLogRow>> ListLogAsync(int limit, CancellationToken ct) =>
        (await RunAsync(connection => connection.QueryAsync<ProcessingLogRow>(new CommandDefinition(
            """
            select id, processed_at as ProcessedAt, file_name as FileName, status, source_format as SourceFormat,
                   invoice_id as InvoiceId, message, errors, duration_ms as DurationMs
            from processing_log
            order by id desc
            limit @limit
            """,
            new { limit }, cancellationToken: ct)))).AsList();

    private static string StatusCode(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Imported => "imported",
        ProcessingStatus.Duplicate => "duplicate",
        ProcessingStatus.ValidationFailed => "validation_failed",
        ProcessingStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>Database problems are never the invoice's fault: report them as transient so the file is retried.</summary>
    private async Task<T> RunAsync<T>(Func<NpgsqlConnection, Task<T>> action)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            return await action(connection);
        }
        catch (NpgsqlException ex)
        {
            throw new TransientProcessingException($"Datenbankfehler: {ex.Message}", ex);
        }
    }
}
