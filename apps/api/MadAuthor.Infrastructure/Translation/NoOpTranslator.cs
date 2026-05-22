using MadAuthor.Application.Translation;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Translation;

/// <summary>
/// Fallback translator used when no provider API key is configured. Always returns null from
/// <see cref="TranslateAsync"/> so the API can surface a 503 to the UI. Logs a warning the first
/// time it's invoked so operators see the missing-config signal without flooding the log.
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
    public string ProviderName => "none";

    public Task<TranslationResult?> TranslateAsync(TranslateRequest request, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _log.LogWarning(
                "Translation requested but no translation provider is configured. " +
                "Set OPENAI_API_KEY or DEEPL_API_KEY in the environment and restart the API.");
        }
        return Task.FromResult<TranslationResult?>(null);
    }
}
