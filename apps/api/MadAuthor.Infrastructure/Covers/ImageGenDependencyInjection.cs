using MadAuthor.Application.Covers;
using Microsoft.Extensions.DependencyInjection;

namespace MadAuthor.Infrastructure.Covers;

/// <summary>
/// Registers image generation without any direct third-party AI provider.
/// MADAuthor routes AI work through MADCloud only; this local implementation
/// returns a controlled 503 until MADCloud has produced a cover asset/callback.
/// </summary>
public static class ImageGenDependencyInjection
{
    public static IServiceCollection AddMadAuthorImageGen(this IServiceCollection services)
    {
        services.AddHttpClient(); // ensures IHttpClientFactory
        services.AddSingleton<IImageGenerator, NoOpImageGenerator>();
        return services;
    }
}
