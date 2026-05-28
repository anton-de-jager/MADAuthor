using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// Wires up local no-op audio/OCR services. MADCloud is the only AI integration
/// boundary for transcription/OCR; local upload extraction remains deterministic
/// and returns null for AI-only media extraction until a MADCloud callback supplies it.
/// </summary>
public static class MediaProcessingDependencyInjection
{
    public static IServiceCollection AddMadAuthorMediaProcessing(this IServiceCollection services)
    {
        // Idempotent - safe to call even if AddHttpClient was already invoked elsewhere.
        services.AddHttpClient();

        services.AddSingleton<IAudioTranscriber, NoOpAudioTranscriber>();
        services.AddSingleton<IOcrService, NoOpOcrService>();
        return services;
    }
}
