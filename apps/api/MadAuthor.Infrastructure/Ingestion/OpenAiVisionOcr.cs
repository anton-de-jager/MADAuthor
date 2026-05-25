using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// OpenAI Vision-based OCR via <c>gpt-4o-mini</c>. Gated on <c>OPENAI_API_KEY</c>; the DI
/// extension only registers this implementation when the key is non-empty.
///
/// The model is asked to emit plain text in reading order with no commentary, so the result can
/// be stored verbatim as <c>BookAsset.ExtractedText</c>.
/// </summary>
public sealed class OpenAiVisionOcr : IOcrService
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private const string Model = "gpt-4o-mini";

    private const string Instruction =
        "Extract all visible text from this image, preserving paragraph breaks and reading order. " +
        "Output ONLY the extracted text - no commentary, no markdown, no headers. " +
        "If there's no text, output an empty string.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OpenAiMediaOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiVisionOcr> _log;

    public OpenAiVisionOcr(
        OpenAiMediaOptions options,
        IHttpClientFactory httpFactory,
        ILogger<OpenAiVisionOcr> log)
    {
        _options = options;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ProviderName => "openai-gpt-4o-mini";

    public async Task<string?> ExtractTextAsync(
        Stream image, string fileName, string? mimeType, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await image.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        if (bytes.Length == 0)
        {
            _log.LogWarning("OCR skipped: {File} is empty.", fileName);
            return null;
        }

        var dataMime = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType;
        var dataUrl = $"data:{dataMime};base64,{Convert.ToBase64String(bytes)}";

        var request = new ChatRequest(
            Model: Model,
            Messages: new[]
            {
                new ChatMessage(
                    Role: "user",
                    Content: new ChatContentPart[]
                    {
                        new ChatTextPart("text", Instruction),
                        new ChatImagePart("image_url", new ChatImageUrl(dataUrl)),
                    }),
            },
            MaxTokens: 4000,
            Temperature: 0);

        using var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        http.Timeout = TimeSpan.FromMinutes(2);

        try
        {
            using var resp = await http.PostAsJsonAsync(Endpoint, request, JsonOpts, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                _log.LogWarning(
                    "OCR API returned {Status} for {File}: {Body}",
                    (int)resp.StatusCode, fileName, body);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<ChatResponse>(stream, JsonOpts, ct);
            var text = payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "OCR failed for {File}.", fileName);
            return null;
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return "<unreadable>"; }
    }

    // --- OpenAI chat-completions request/response shape ---------------------------------------
    // The content array is a discriminated union of text + image parts; System.Text.Json handles
    // it transparently because we hand-author the records as a heterogeneous array.

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<ChatContentPart> Content);

    private abstract record ChatContentPart(
        [property: JsonPropertyName("type")] string Type);

    private sealed record ChatTextPart(
        string Type,
        [property: JsonPropertyName("text")] string Text) : ChatContentPart(Type);

    private sealed record ChatImagePart(
        string Type,
        [property: JsonPropertyName("image_url")] ChatImageUrl ImageUrl) : ChatContentPart(Type);

    private sealed record ChatImageUrl(
        [property: JsonPropertyName("url")] string Url);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content);
}
