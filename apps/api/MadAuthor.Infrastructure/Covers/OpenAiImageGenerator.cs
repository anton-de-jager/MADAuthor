using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MadAuthor.Application.Covers;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Covers;

public sealed class OpenAiImageGeneratorOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "dall-e-3";
}

/// <summary>
/// DALL-E 3 image generator. Posts to <c>/v1/images/generations</c> and returns the
/// base64-decoded PNG. DALL-E only supports three sizes (<c>1024x1024</c>, <c>1792x1024</c>,
/// <c>1024x1792</c>); the requested width/height are snapped to the closest match. DALL-E
/// also rewrites the prompt server-side for safety/quality, and that rewritten prompt is
/// returned via <see cref="GeneratedImage.PromptUsed"/> so attribution stays honest.
/// </summary>
public sealed class OpenAiImageGenerator : IImageGenerator
{
    private const string Endpoint = "https://api.openai.com/v1/images/generations";

    private readonly OpenAiImageGeneratorOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiImageGenerator> _log;

    public OpenAiImageGenerator(
        OpenAiImageGeneratorOptions options,
        IHttpClientFactory httpFactory,
        ILogger<OpenAiImageGenerator> log)
    {
        _options = options;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ProviderName => $"openai-{_options.Model}";

    public async Task<GeneratedImage?> GenerateAsync(GenerateImageRequest request, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            _log.LogWarning("OpenAiImageGenerator.GenerateAsync called but OPENAI_API_KEY is empty.");
            return null;
        }

        var size = SnapToDalleSize(request.Width, request.Height);
        var body = new RequestBody(
            Model: _options.Model,
            Prompt: BuildPrompt(request),
            N: 1,
            Size: size,
            Quality: "standard",
            ResponseFormat: "b64_json");

        // Per-call client — we set Authorization headers + an extended timeout that we don't
        // want to leak into the shared singleton HttpClient.
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var resp = await http.PostAsJsonAsync(Endpoint, body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                _log.LogError(
                    "OpenAI image generation failed ({Status}): {Body}",
                    (int)resp.StatusCode, errBody);
                return null;
            }

            var parsed = await resp.Content.ReadFromJsonAsync<ResponseBody>(cancellationToken: ct);
            var first = parsed?.Data?.FirstOrDefault();
            if (first is null || string.IsNullOrWhiteSpace(first.B64Json))
            {
                _log.LogError("OpenAI image generation returned no data payload.");
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(first.B64Json);
            }
            catch (FormatException ex)
            {
                _log.LogError(ex, "OpenAI returned malformed base64 image data.");
                return null;
            }

            var promptUsed = string.IsNullOrWhiteSpace(first.RevisedPrompt)
                ? body.Prompt
                : first.RevisedPrompt;

            return new GeneratedImage(
                PngBytes: bytes,
                PromptUsed: promptUsed,
                Provider: ProviderName,
                ModelVersion: _options.Model);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OpenAI image generation threw.");
            return null;
        }
    }

    /// <summary>
    /// Fold the optional style hint and "no typography" instruction into a single prompt string.
    /// The negative-prompt knob is not supported by DALL-E 3, so we append it as a natural-language
    /// instruction instead — best-effort.
    /// </summary>
    private static string BuildPrompt(GenerateImageRequest req)
    {
        var parts = new List<string> { req.Prompt };
        if (!string.IsNullOrWhiteSpace(req.StyleHint)) parts.Add($"Style: {req.StyleHint}.");
        if (!string.IsNullOrWhiteSpace(req.NegativePrompt)) parts.Add($"Avoid: {req.NegativePrompt}.");
        return string.Join(' ', parts);
    }

    /// <summary>
    /// DALL-E 3 accepts only 1024x1024, 1792x1024, or 1024x1792. Pick by aspect ratio.
    /// </summary>
    internal static string SnapToDalleSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return "1024x1792";
        var ratio = (double)width / height;
        // Closest of 1.0 (square), 1.75 (landscape), 0.571 (portrait)
        var dSquare = Math.Abs(ratio - 1.0);
        var dLandscape = Math.Abs(ratio - (1792.0 / 1024.0));
        var dPortrait = Math.Abs(ratio - (1024.0 / 1792.0));
        if (dPortrait <= dSquare && dPortrait <= dLandscape) return "1024x1792";
        if (dLandscape <= dSquare && dLandscape <= dPortrait) return "1792x1024";
        return "1024x1024";
    }

    private sealed record RequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("n")] int N,
        [property: JsonPropertyName("size")] string Size,
        [property: JsonPropertyName("quality")] string Quality,
        [property: JsonPropertyName("response_format")] string ResponseFormat);

    private sealed record ResponseBody(
        [property: JsonPropertyName("data")] List<ImageData>? Data);

    private sealed record ImageData(
        [property: JsonPropertyName("b64_json")] string? B64Json,
        [property: JsonPropertyName("revised_prompt")] string? RevisedPrompt);
}
