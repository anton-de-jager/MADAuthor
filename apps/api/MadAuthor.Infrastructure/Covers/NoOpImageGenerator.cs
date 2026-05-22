using MadAuthor.Application.Covers;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Covers;

/// <summary>
/// Fallback image generator used when no provider API key is configured. Always returns null
/// from <see cref="GenerateAsync"/> so the API can surface a 503 to the UI. Logs a warning the
/// first time it's invoked so operators see the missing-config signal without flooding the log.
/// </summary>
public sealed class NoOpImageGenerator : IImageGenerator
{
    private readonly ILogger<NoOpImageGenerator> _log;
    private int _warned;

    public NoOpImageGenerator(ILogger<NoOpImageGenerator> log)
    {
        _log = log;
    }

    public bool IsEnabled => false;
    public string ProviderName => "none";

    public Task<GeneratedImage?> GenerateAsync(GenerateImageRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _log.LogWarning(
                "AI cover generation requested but no image-gen provider is configured. " +
                "Set OPENAI_API_KEY or STABILITY_API_KEY in the environment and restart the API.");
        }
        return Task.FromResult<GeneratedImage?>(null);
    }
}
