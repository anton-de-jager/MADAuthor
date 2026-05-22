using System.Net.Http.Headers;
using MadAuthor.Application.Covers;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Covers;

public sealed class StabilityImageGeneratorOptions
{
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Optional pinned model — defaults to Stable Image Core's current version.</summary>
    public string Model { get; set; } = "stable-image-core";
}

/// <summary>
/// Stability AI Stable Image Core generator. Posts a multipart/form-data body to
/// <c>/v2beta/stable-image/generate/core</c>. The width/height pair from the caller is mapped
/// to the closest supported aspect_ratio. Returns raw PNG bytes when <c>Accept: image/*</c>
/// is sent.
/// </summary>
public sealed class StabilityImageGenerator : IImageGenerator
{
    private const string Endpoint = "https://api.stability.ai/v2beta/stable-image/generate/core";

    private readonly StabilityImageGeneratorOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StabilityImageGenerator> _log;

    public StabilityImageGenerator(
        StabilityImageGeneratorOptions options,
        IHttpClientFactory httpFactory,
        ILogger<StabilityImageGenerator> log)
    {
        _options = options;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ProviderName => $"stability-{_options.Model}";

    public async Task<GeneratedImage?> GenerateAsync(GenerateImageRequest request, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            _log.LogWarning("StabilityImageGenerator.GenerateAsync called but STABILITY_API_KEY is empty.");
            return null;
        }

        var aspectRatio = SnapToAspectRatio(request.Width, request.Height);
        var prompt = BuildPrompt(request);

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        // image/* tells Stable Image Core to return raw image bytes; application/json would
        // return a base64-wrapped JSON envelope instead.
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        // Stability's v2beta endpoints reject application/x-www-form-urlencoded — must be
        // multipart/form-data with the API key in Authorization (not in the form). The empty
        // "none"-form trick is needed because the API expects a file part to be possible.
        using var form = new MultipartFormDataContent
        {
            { new StringContent(prompt), "prompt" },
            { new StringContent(aspectRatio), "aspect_ratio" },
            { new StringContent("png"), "output_format" },
        };
        if (!string.IsNullOrWhiteSpace(request.NegativePrompt))
        {
            form.Add(new StringContent(request.NegativePrompt), "negative_prompt");
        }

        try
        {
            using var resp = await http.PostAsync(Endpoint, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                _log.LogError(
                    "Stability image generation failed ({Status}): {Body}",
                    (int)resp.StatusCode, errBody);
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
            {
                _log.LogError("Stability image generation returned an empty body.");
                return null;
            }

            return new GeneratedImage(
                PngBytes: bytes,
                PromptUsed: prompt,
                Provider: ProviderName,
                ModelVersion: _options.Model);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stability image generation threw.");
            return null;
        }
    }

    private static string BuildPrompt(GenerateImageRequest req)
    {
        var parts = new List<string> { req.Prompt };
        if (!string.IsNullOrWhiteSpace(req.StyleHint)) parts.Add($"Style: {req.StyleHint}.");
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Stable Image Core accepts one of a fixed set of aspect ratios. Pick the closest by
    /// numeric ratio. Portrait 2:3 is the natural default for book covers.
    /// </summary>
    internal static string SnapToAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0) return "2:3";
        var target = (double)width / height;
        var options = new (string Label, double Ratio)[]
        {
            ("21:9", 21.0 / 9.0),
            ("16:9", 16.0 / 9.0),
            ("3:2", 3.0 / 2.0),
            ("5:4", 5.0 / 4.0),
            ("1:1", 1.0),
            ("4:5", 4.0 / 5.0),
            ("2:3", 2.0 / 3.0),
            ("9:16", 9.0 / 16.0),
            ("9:21", 9.0 / 21.0),
        };
        var best = options[0];
        var bestDelta = Math.Abs(options[0].Ratio - target);
        for (var i = 1; i < options.Length; i++)
        {
            var delta = Math.Abs(options[i].Ratio - target);
            if (delta < bestDelta)
            {
                best = options[i];
                bestDelta = delta;
            }
        }
        return best.Label;
    }
}
