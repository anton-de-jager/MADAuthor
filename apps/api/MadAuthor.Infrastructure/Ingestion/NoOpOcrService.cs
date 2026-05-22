using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// Fallback OCR used when no vision provider is configured (e.g. <c>OPENAI_API_KEY</c> is not
/// set). Returns <c>null</c> for every input and logs a one-shot warning on first call.
/// </summary>
public sealed class NoOpOcrService(ILogger<NoOpOcrService> log) : IOcrService
{
    private int _warned;

    public bool IsEnabled => false;
    public string ProviderName => "noop";

    public Task<string?> ExtractTextAsync(
        Stream image, string fileName, string? mimeType, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            log.LogWarning(
                "OCR is disabled: OPENAI_API_KEY is not set. " +
                "Image uploads will be stored without text extraction.");
        }
        return Task.FromResult<string?>(null);
    }
}
