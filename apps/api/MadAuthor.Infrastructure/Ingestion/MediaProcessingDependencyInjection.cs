using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// Wires up the audio-transcription and image-OCR services. Both light up automatically when
/// <c>OPENAI_API_KEY</c> is set in the environment; otherwise registers no-op implementations
/// that return <c>null</c> so the upload pipeline keeps working without external credentials.
/// </summary>
public static class MediaProcessingDependencyInjection
{
    public static IServiceCollection AddMadAuthorMediaProcessing(this IServiceCollection services)
    {
        // IHttpClientFactory is shared by Whisper + Vision clients. Idempotent — safe to call
        // even if AddHttpClient was already invoked elsewhere.
        services.AddHttpClient();

        var openAi = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAi))
        {
            services.AddSingleton(new OpenAiMediaOptions { ApiKey = openAi });
            services.AddSingleton<IAudioTranscriber, OpenAiWhisperTranscriber>();
            services.AddSingleton<IOcrService, OpenAiVisionOcr>();
        }
        else
        {
            services.AddSingleton<IAudioTranscriber, NoOpAudioTranscriber>();
            services.AddSingleton<IOcrService, NoOpOcrService>();
        }
        return services;
    }
}

public sealed class OpenAiMediaOptions
{
    public required string ApiKey { get; init; }
}
