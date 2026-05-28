using MadAuthor.Application.Translation;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Translation;

/// <summary>
/// Local placeholder because MADAuthor does not call translation AI providers directly.
/// Translation work must be queued through MADCloud and delivered back as translated chapters.
/// </summary>
public sealed class NoOpTranslator : ITranslator
{
    private readonly ILogger<NoOpTranslator> _log;
    private int _warned;

    public NoOpTranslator(ILogger<NoOpTranslator> log)
    {
        _log = log;
    }

    public bool IsEnabled => false;
    public string ProviderName => "MADCloud";

    public Task<TranslationResult?> TranslateAsync(TranslateRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _log.LogWarning(
                "Translation requested locally. MADAuthor uses MADCloud as the only AI integration; " +
                "queue translation through MADCloud and persist the returned text.");
        }
        return Task.FromResult<TranslationResult?>(null);
    }
}
