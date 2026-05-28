using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// Local placeholder because MADAuthor does not call audio AI providers directly.
/// Transcription must be queued through MADCloud and delivered back as extracted text.
/// </summary>
public sealed class NoOpAudioTranscriber(ILogger<NoOpAudioTranscriber> log) : IAudioTranscriber
{
    private int _warned;

    public bool IsEnabled => false;
    public string ProviderName => "MADCloud";

    public Task<AudioTranscription?> TranscribeAsync(
        Stream audio, string fileName, string? mimeType, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            log.LogWarning(
                "Audio transcription requested locally. MADAuthor uses MADCloud as the only AI integration; " +
                "audio uploads will be stored until MADCloud returns transcription text.");
        }
        return Task.FromResult<AudioTranscription?>(null);
    }
}
