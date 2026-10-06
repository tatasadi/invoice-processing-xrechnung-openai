using InvoiceProcessing.Core.Model;
using InvoiceProcessing.Core.Processing;
using InvoiceProcessing.Core.Reading;
using InvoiceProcessing.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceProcessing.Tests;

public class InvoiceProcessorTests
{
    private static readonly Invoice Correct = TestData.Invoice(TestData.Line(10, 6.49m, 64.90m));

    // The same invoice with the VAT misread by one digit (as observed once with gpt-5.4-mini: 184,27 instead of 185,27).
    private static readonly Invoice Misread = Correct with { TaxTotal = Correct.TaxTotal - 1m };

    [Fact]
    public async Task Ai_misread_is_corrected_by_a_second_read()
    {
        var reader = new SequenceReader(retry: true, Misread, Correct);

        var outcome = await Processor(reader).ProcessAsync(Document(), CancellationToken.None);

        Assert.Equal(ProcessingStatus.Imported, outcome.Status);
        Assert.Equal(2, reader.Reads);
        Assert.StartsWith("Prüfung beim zweiten Lesen bestanden", outcome.Message);
        Assert.EndsWith("zweimal gelesen", outcome.Description);
    }

    [Fact]
    public async Task Invoice_that_fails_twice_is_rejected()
    {
        var reader = new SequenceReader(retry: true, Misread, Misread);

        var outcome = await Processor(reader).ProcessAsync(Document(), CancellationToken.None);

        Assert.Equal(ProcessingStatus.ValidationFailed, outcome.Status);
        Assert.Equal(2, reader.Reads);
        Assert.Contains("auch nach zweitem Lesen", outcome.Message);
    }

    [Fact]
    public async Task Deterministic_reader_is_not_read_twice()
    {
        var reader = new SequenceReader(retry: false, Misread, Correct);

        var outcome = await Processor(reader).ProcessAsync(Document(), CancellationToken.None);

        Assert.Equal(ProcessingStatus.ValidationFailed, outcome.Status);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task Valid_invoice_is_read_once()
    {
        var reader = new SequenceReader(retry: true, Correct);

        var outcome = await Processor(reader).ProcessAsync(Document(), CancellationToken.None);

        Assert.Equal(ProcessingStatus.Imported, outcome.Status);
        Assert.Equal(1, reader.Reads);
        Assert.StartsWith("Prüfung bestanden", outcome.Message);
    }

    private static InvoiceDocument Document() => InvoiceDocument.FromBytes("rechnung.pdf", [1, 2, 3]);

    private static InvoiceProcessor Processor(IInvoiceReader reader) => new(
        [reader],
        new InvoiceValidator(new ValidationOptions(), TimeProvider.System),
        new InMemoryRepository(),
        new NoArchive(),
        NullLogger<InvoiceProcessor>.Instance);

    private sealed class SequenceReader(bool retry, params Invoice[] results) : IInvoiceReader
    {
        public int Reads { get; private set; }

        public bool ReadAgainOnValidationFailure => retry;

        public bool CanRead(InvoiceDocument document) => true;

        public Task<ExtractionResult> ReadAsync(InvoiceDocument document, CancellationToken ct) =>
            Task.FromResult(new ExtractionResult(results[Math.Min(Reads++, results.Length - 1)], "Test", "Testleser"));
    }

    private sealed class InMemoryRepository : IInvoiceRepository
    {
        private long _nextId = 1;

        public Task<ExistingInvoice?> FindByFileHashAsync(string sha256, CancellationToken ct) => Task.FromResult<ExistingInvoice?>(null);

        public Task<ExistingInvoice?> FindDuplicateAsync(Invoice invoice, InvoiceKeys keys, CancellationToken ct) => Task.FromResult<ExistingInvoice?>(null);

        public Task<long> InsertAsync(Invoice invoice, InvoiceSource source, CancellationToken ct) => Task.FromResult(_nextId++);

        public Task LogAsync(ProcessingLogEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoArchive : IDocumentArchive
    {
        public Task<string> StoreAsync(InvoiceDocument document, Invoice invoice, CancellationToken ct) => Task.FromResult("test.pdf");

        public void Delete(string relativePath) { }

        public string GetFullPath(string relativePath) => relativePath;
    }
}
