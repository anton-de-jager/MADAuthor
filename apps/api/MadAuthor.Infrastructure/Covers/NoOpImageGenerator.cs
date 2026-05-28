using MadAuthor.Application.Covers;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Covers;

/// <summary>
/// Local placeholder because MADAuthor does not call AI image providers directly.
/// Cover AI work must be queued through MADCloud and delivered back as an asset/callback.
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
    public string ProviderName => "MADCloud";

    public Task<GeneratedImage?> GenerateAsync(GenerateImageRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _log.LogWarning(
                "AI cover generation requested locally. MADAuthor uses MADCloud as the only AI integration; " +
                "queue cover generation through MADCloud and persist the returned asset.");
        }
        return Task.FromResult<GeneratedImage?>(null);
    }
}
