using MadAuthor.Application.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace MadAuthor.Infrastructure.Translation;

/// <summary>
/// Registers the active <see cref="ITranslator"/> based on which provider API key is present
/// in the environment. OpenAI takes precedence over DeepL; if neither is set we fall back to
/// <see cref="NoOpTranslator"/> so the controller can return a 503 instead of crashing the
/// request pipeline.
/// </summary>
public static class TranslationDependencyInjection
{
    public static IServiceCollection AddMadAuthorTranslation(this IServiceCollection services)
    {
        services.AddHttpClient(); // ensures IHttpClientFactory is registered

        var openAi = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var deepL = Environment.GetEnvironmentVariable("DEEPL_API_KEY");

        if (!string.IsNullOrWhiteSpace(openAi))
        {
            services.AddSingleton(new OpenAiTranslatorOptions { ApiKey = openAi });
            services.AddSingleton<ITranslator, OpenAiTranslator>();
        }
        else if (!string.IsNullOrWhiteSpace(deepL))
        {
            services.AddSingleton(new DeepLTranslatorOptions { ApiKey = deepL });
            services.AddSingleton<ITranslator, DeepLTranslator>();
        }
        else
        {
            services.AddSingleton<ITranslator, NoOpTranslator>();
        }
        return services;
    }
}
