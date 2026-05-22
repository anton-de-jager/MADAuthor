namespace MadAuthor.Application.Ingestion;

public interface IAudioTranscriber
{
    bool IsEnabled { get; }
    string ProviderName { get; }

    /// <summary>
    /// Transcribe an audio stream. Returns plain text (no timestamps) for the body, and the
    /// detected language. Caller is responsible for stream lifecycle.
    /// </summary>
    Task<AudioTranscription?> TranscribeAsync(
        Stream audio, string fileName, string? mimeType, CancellationToken ct = default);
}

public sealed record AudioTranscription(string Text, string? Language, string Provider);
