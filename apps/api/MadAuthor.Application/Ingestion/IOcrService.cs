namespace MadAuthor.Application.Ingestion;

public interface IOcrService
{
    bool IsEnabled { get; }
    string ProviderName { get; }

    /// <summary>
    /// Extract text from an image. Returns plain text in reading order, or null if no text was
    /// detected or extraction is unavailable.
    /// </summary>
    Task<string?> ExtractTextAsync(
        Stream image, string fileName, string? mimeType, CancellationToken ct = default);
}
