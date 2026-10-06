using System.Security.Cryptography;

namespace InvoiceProcessing.Core.Reading;

/// <summary>An incoming file with its content and SHA-256 hash (used for duplicate detection).</summary>
public sealed class InvoiceDocument
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
    public required string Sha256 { get; init; }

    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();

    public static InvoiceDocument FromBytes(string fileName, byte[] content) => new()
    {
        FileName = fileName,
        Content = content,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
    };

    public static async Task<InvoiceDocument> LoadAsync(string path, CancellationToken ct = default) =>
        FromBytes(Path.GetFileName(path), await File.ReadAllBytesAsync(path, ct));
}
