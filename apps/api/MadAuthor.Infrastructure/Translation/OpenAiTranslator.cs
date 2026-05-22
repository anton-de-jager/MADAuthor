using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MadAuthor.Application.Translation;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Translation;

public sealed class OpenAiTranslatorOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

/// <summary>
/// OpenAI chat-completions Markdown translator. Posts to <c>/v1/chat/completions</c> with a
/// system prompt that pins the model to preserve Markdown structure (headings, lists, emphasis,
/// code blocks, quotes) and translate everything else. Temperature is kept low (0.3) to keep
/// translations stable across re-runs without making the output overly literal.
/// </summary>
public sealed class OpenAiTranslator : ITranslator
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private readonly OpenAiTranslatorOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiTranslator> _log;

    public OpenAiTranslator(
        OpenAiTranslatorOptions options,
        IHttpClientFactory httpFactory,
        ILogger<OpenAiTranslator> log)
    {
        _options = options;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ProviderName => $"openai-{_options.Model}";

    public async Task<TranslationResult?> TranslateAsync(TranslateRequest request, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            _log.LogWarning("OpenAiTranslator.TranslateAsync called but OPENAI_API_KEY is empty.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(request.SourceMarkdown))
        {
            return new TranslationResult(string.Empty, request.SourceLanguage ?? "auto", ProviderName);
        }

        var systemPrompt = BuildSystemPrompt(request);

        // Some languages (e.g. German, Japanese in romaji) routinely expand the source by 30-50%.
        // Multiply by 1.5 and ensure a sensible floor so very short chapters still translate.
        // Each token ~= 4 characters; we cap at 16k to stay well under gpt-4o-mini's 128k context.
        var estimatedInputTokens = Math.Max(256, request.SourceMarkdown.Length / 4);
        var maxTokens = Math.Min(16000, (int)(estimatedInputTokens * 1.5) + 256);

        var body = new RequestBody(
            Model: _options.Model,
            Messages: new[]
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", request.SourceMarkdown),
            },
            Temperature: 0.3,
            MaxTokens: maxTokens);

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
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
                    "OpenAI translation failed ({Status}): {Body}",
                    (int)resp.StatusCode, errBody);
                return null;
            }

            var parsed = await resp.Content.ReadFromJsonAsync<ResponseBody>(cancellationToken: ct);
            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                _log.LogError("OpenAI translation returned empty content.");
                return null;
            }

            return new TranslationResult(
                TranslatedMarkdown: content.Trim(),
                SourceLanguageDetected: request.SourceLanguage ?? "auto",
                Provider: ProviderName);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OpenAI translation threw.");
            return null;
        }
    }

    private static string BuildSystemPrompt(TranslateRequest req)
    {
        var sourceLang = string.IsNullOrWhiteSpace(req.SourceLanguage)
            ? "the source language"
            : req.SourceLanguage;
        var style = string.IsNullOrWhiteSpace(req.StyleHint)
            ? "maintain the source text's register"
            : req.StyleHint;

        return
            $"You are a professional literary translator. Translate the following Markdown text from {sourceLang} to {req.TargetLanguage}.\n\n" +
            "Rules:\n" +
            "- Preserve ALL Markdown syntax: # ## ### headings, **bold**, *italic*, > quotes, lists, code blocks. The output must be valid Markdown matching the input structure.\n" +
            "- Preserve all line breaks and paragraph breaks.\n" +
            "- Translate idioms naturally. Do not translate code blocks, URLs, or proper nouns that don't have target-language equivalents.\n" +
            $"- Match this voice: {style}.\n" +
            "- Output ONLY the translated Markdown. No commentary, no explanations, no '---' before/after.";
    }

    private sealed record RequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseBody(
        [property: JsonPropertyName("choices")] List<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
