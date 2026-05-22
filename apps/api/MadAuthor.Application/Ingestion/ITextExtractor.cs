namespace MadAuthor.Application.Ingestion;

public interface ITextExtractor
{
    /// <summary>
    /// Best-effort extract plain text from an uploaded file. Returns null when the MIME type
    /// isn't supported or extraction fails.
    /// </summary>
    Task<string?> ExtractAsync(string mimeType, string fileName, Stream content, CancellationToken ct = default);
}
