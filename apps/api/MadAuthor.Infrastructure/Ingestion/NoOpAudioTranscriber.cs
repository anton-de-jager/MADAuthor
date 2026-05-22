using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// Fallback transcriber used when no audio provider is configured (e.g. <c>OPENAI_API_KEY</c>
/// is not set). Returns <c>null</c> for every input and logs a one-shot warning on first call so
/// operators see why audio uploads aren't being transcribed.
/// </summary>
public sealed class NoOpAudioTranscriber(ILogger<NoOpAudioTranscriber> log) : IAudioTranscriber
{
    private int _warned;

    public bool IsEnabled => false;
    public string ProviderName => "noop";

    public Task<AudioTranscription?> TranscribeAsync(
        Stream audio, string fileName, string? mimeType, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            log.LogWarning(
                "Audio transcription is disabled: OPENAI_API_KEY is not set. " +
                "Audio uploads will be stored without transcription.");
        }
        return Task.FromResult<AudioTranscription?>(null);
    }
}
