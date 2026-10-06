using InvoiceProcessing.Core.Model;

namespace InvoiceProcessing.Core.Processing;

public enum ProcessingStatus
{
    Imported,
    Duplicate,
    ValidationFailed,
    Failed,

    /// <summary>Temporary problem (database or OpenAI not reachable, missing configuration); the file can simply be imported again later.</summary>
    Retry,
}

public sealed record ProcessingOutcome(ProcessingStatus Status, string Message)
{
    public IReadOnlyList<string> Errors { get; init; } = [];
    public long? InvoiceId { get; init; }

    /// <summary>Short source format, e.g. "XRechnung UBL" or "PDF via OpenAI".</summary>
    public string? Format { get; init; }

    /// <summary>How the file was read, e.g. which OpenAI model and how many tokens.</summary>
    public string? Description { get; init; }

    /// <summary>The extracted invoice, also when validation failed, so the user can see what was read.</summary>
    public Invoice? Invoice { get; init; }

    public TimeSpan Duration { get; init; }
}
