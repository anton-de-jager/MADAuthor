using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// Local placeholder because MADAuthor does not call OCR/vision AI providers directly.
/// OCR must be queued through MADCloud and delivered back as extracted text.
/// </summary>
public sealed class NoOpOcrService(ILogger<NoOpOcrService> log) : IOcrService
{
    private int _warned;

    public bool IsEnabled => false;
    public string ProviderName => "MADCloud";

    public Task<string?> ExtractTextAsync(
        Stream image, string fileName, string? mimeType, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            log.LogWarning(
                "OCR requested locally. MADAuthor uses MADCloud as the only AI integration; " +
                "image uploads will be stored until MADCloud returns extracted text.");
        }
        return Task.FromResult<string?>(null);
    }
}
