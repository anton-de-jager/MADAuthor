using MadAuthor.Application.Covers;
using Microsoft.Extensions.DependencyInjection;

namespace MadAuthor.Infrastructure.Covers;

/// <summary>
/// Registers the active <see cref="IImageGenerator"/> based on which provider API key is
/// present in the environment. OpenAI takes precedence over Stability; if neither is set
/// we fall back to <see cref="NoOpImageGenerator"/> so the controller can return a 503
/// instead of crashing the request pipeline.
/// </summary>
public static class ImageGenDependencyInjection
{
    public static IServiceCollection AddMadAuthorImageGen(this IServiceCollection services)
    {
        // Provider auto-selection by environment variable (OpenAI takes precedence).
        var openAi = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var stability = Environment.GetEnvironmentVariable("STABILITY_API_KEY");

        services.AddHttpClient(); // ensures IHttpClientFactory

        if (!string.IsNullOrWhiteSpace(openAi))
        {
            services.AddSingleton(new OpenAiImageGeneratorOptions { ApiKey = openAi });
            services.AddSingleton<IImageGenerator, OpenAiImageGenerator>();
        }
        else if (!string.IsNullOrWhiteSpace(stability))
        {
            services.AddSingleton(new StabilityImageGeneratorOptions { ApiKey = stability });
            services.AddSingleton<IImageGenerator, StabilityImageGenerator>();
        }
        else
        {
            services.AddSingleton<IImageGenerator, NoOpImageGenerator>();
        }
        return services;
    }
}
