using MadAuthor.Application.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace MadAuthor.Infrastructure.Translation;

/// <summary>
/// Registers translation through the MADCloud-only boundary. The local translator
/// returns a controlled 503 until translation output is supplied by MADCloud.
/// </summary>
public static class TranslationDependencyInjection
{
    public static IServiceCollection AddMadAuthorTranslation(this IServiceCollection services)
    {
        services.AddHttpClient(); // ensures IHttpClientFactory is registered
        services.AddSingleton<ITranslator, NoOpTranslator>();
        return services;
    }
}
