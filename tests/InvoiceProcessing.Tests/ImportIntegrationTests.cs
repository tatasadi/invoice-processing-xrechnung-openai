using Dapper;
using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Infrastructure;
using InvoiceProcessing.Infrastructure.Import;
using InvoiceProcessing.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using s2industries.ZUGFeRD;

namespace InvoiceProcessing.Tests;

/// <summary>
/// End-to-end import against a real PostgreSQL (docker compose up -d). Uses its own database "rechnungen_test".
/// Run only the unit tests with: dotnet test --filter Category!=Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed class ImportIntegrationTests : IClassFixture<ImportIntegrationTests.Fixture>, IAsyncLifetime
{
    private readonly Fixture _fixture;

    public ImportIntegrationTests(Fixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("truncate processing_log, invoice_lines, invoices restart identity cascade");
        foreach (var folder in new[] { _fixture.Storage.InboxPath, _fixture.Storage.ArchivePath, _fixture.Storage.FailedPath, _fixture.Storage.DuplicatesPath })
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task XRechnung_is_stored_with_lines_log_entry_and_archived_original()
    {
        var outcome = await _fixture.Import.ImportFileAsync(TestData.File("02_"), CancellationToken.None);

        Assert.Equal(ProcessingStatus.Imported, outcome.Status);
        var stored = Assert.Single(await _fixture.Queries.ListInvoicesAsync(10, CancellationToken.None));
        Assert.Equal("2026-4418", stored.InvoiceNumber);
        Assert.Equal(853.49m, stored.GrossTotal);
        Assert.Equal(4, stored.LineCount);
        Assert.True(File.Exists(Path.Combine(_fixture.Storage.ArchivePath, stored.ArchivePath!)));

        var lines = await _fixture.Queries.ListLinesAsync(stored.Id, CancellationToken.None);
        Assert.Equal(51.60m, lines[0].DiscountAmount);

        var log = Assert.Single(await _fixture.Queries.ListLogAsync(10, CancellationToken.None));
        Assert.Equal("imported", log.Status);
    }

    [Fact]
    public async Task Same_file_twice_is_a_duplicate()
    {
        await _fixture.Import.ImportFileAsync(TestData.File("01_"), CancellationToken.None);
        var second = await _fixture.Import.ImportFileAsync(TestData.File("01_"), CancellationToken.None);

        Assert.Equal(ProcessingStatus.Duplicate, second.Status);
        Assert.Contains("Identische Datei", second.Message);
        Assert.Single(await _fixture.Queries.ListInvoicesAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Same_invoice_in_another_format_is_a_duplicate()
    {
        await _fixture.Import.ImportFileAsync(TestData.File("01_"), CancellationToken.None);

        // The same invoice saved as CII instead of UBL: different file, same supplier and invoice number.
        var otherFormat = Path.Combine(_fixture.Storage.InboxPath, "kaltenbrunn-als-cii.xml");
        InvoiceDescriptor.Load(TestData.File("01_")).Save(otherFormat, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.CII);
        var second = await _fixture.Import.ImportFileAsync(otherFormat, CancellationToken.None);

        Assert.Equal(ProcessingStatus.Duplicate, second.Status);
        Assert.Contains("bereits importiert", second.Message);
    }

    [Fact]
    public async Task Inbox_files_are_cleaned_up_after_processing()
    {
        File.Copy(TestData.File("03_"), Path.Combine(_fixture.Storage.InboxPath, "03.pdf"));
        await File.WriteAllTextAsync(Path.Combine(_fixture.Storage.InboxPath, "notiz.txt"), "keine Rechnung");

        foreach (var file in _fixture.Import.GetInboxFiles())
            await _fixture.Import.ImportFromInboxAsync(file, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_fixture.Storage.InboxPath));
        Assert.True(File.Exists(Path.Combine(_fixture.Storage.FailedPath, "notiz.txt")));
        Assert.True(File.Exists(Path.Combine(_fixture.Storage.FailedPath, "notiz.txt.fehler.txt")));
        Assert.Single(Directory.GetFiles(_fixture.Storage.ArchivePath, "*.pdf", SearchOption.AllDirectories));
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private const string ServerConnection = "Host=localhost;Port=5432;Database=rechnungen;Username=rechnungen;Password=rechnungen_local";
        private const string TestDatabase = "rechnungen_test";
        private ServiceProvider? _services;

        public NpgsqlDataSource DataSource => _services!.GetRequiredService<NpgsqlDataSource>();
        public InvoiceImportService Import => _services!.GetRequiredService<InvoiceImportService>();
        public IInvoiceQueries Queries => _services!.GetRequiredService<IInvoiceQueries>();
        public StorageOptions Storage { get; } = new();

        public async Task InitializeAsync()
        {
            var server = Environment.GetEnvironmentVariable("INVOICE_TEST_DB") ?? ServerConnection;
            await using (var connection = new NpgsqlConnection(server))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync($"drop database if exists {TestDatabase} with (force)");
                await connection.ExecuteAsync($"create database {TestDatabase}");
            }

            var root = Path.Combine(Path.GetTempPath(), "rechnungsverarbeitung-tests");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:InvoiceDb"] = new NpgsqlConnectionStringBuilder(server) { Database = TestDatabase }.ConnectionString,
                ["Storage:InboxPath"] = Path.Combine(root, "Eingang"),
                ["Storage:ArchivePath"] = Path.Combine(root, "Archiv"),
                ["Storage:FailedPath"] = Path.Combine(root, "Fehler"),
                ["Storage:DuplicatesPath"] = Path.Combine(root, "Duplikate"),
            }).Build();
            configuration.GetSection("Storage").Bind(Storage);

            _services = new ServiceCollection()
                .AddLogging()
                .AddInvoiceProcessing(configuration)
                .BuildServiceProvider();
            await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            if (_services is not null) await _services.DisposeAsync();
        }
    }
}
