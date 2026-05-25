using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MadAuthor.Application.Translation;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Translation;

public sealed class DeepLTranslatorOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// DeepL Markdown translator. DeepL preserves Markdown reasonably well when called with
/// <c>preserve_formatting=1</c> - line breaks, list markers, and emphasis tend to survive,
/// though heading hashes and fenced code blocks are not guaranteed. We don't use
/// <c>tag_handling=xml</c> because that mode expects XML/HTML tags, not Markdown.
///
/// Free vs paid: DeepL routes free-tier accounts via <c>api-free.deepl.com</c>. Their docs
/// say a free API key ends with the literal suffix <c>:fx</c>; we detect that suffix to
/// select the right host.
/// </summary>
public sealed class DeepLTranslator : ITranslator
{
    private const string PaidEndpoint = "https://api.deepl.com/v2/translate";
    private const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";

    private readonly DeepLTranslatorOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DeepLTranslator> _log;

    public DeepLTranslator(
        DeepLTranslatorOptions options,
        IHttpClientFactory httpFactory,
        ILogger<DeepLTranslator> log)
    {
        _options = options;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ProviderName => "deepl";

    public async Task<TranslationResult?> TranslateAsync(TranslateRequest request, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            _log.LogWarning("DeepLTranslator.TranslateAsync called but DEEPL_API_KEY is empty.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(request.SourceMarkdown))
        {
            return new TranslationResult(string.Empty, request.SourceLanguage ?? "auto", ProviderName);
        }

        var endpoint = _options.ApiKey.EndsWith(":fx", StringComparison.Ordinal)
            ? FreeEndpoint
            : PaidEndpoint;

        var targetCode = MapToDeepLLanguageCode(request.TargetLanguage);
        if (string.IsNullOrWhiteSpace(targetCode))
        {
            _log.LogWarning("DeepL: could not map target language '{Lang}' to a DeepL code.", request.TargetLanguage);
            return null;
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("text", request.SourceMarkdown),
            new("target_lang", targetCode),
            new("preserve_formatting", "1"),
        };
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            var sourceCode = MapToDeepLLanguageCode(request.SourceLanguage);
            if (!string.IsNullOrWhiteSpace(sourceCode))
                form.Add(new KeyValuePair<string, string>("source_lang", sourceCode));
        }

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("DeepL-Auth-Key", _options.ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var resp = await http.PostAsync(endpoint, content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                _log.LogError("DeepL translation failed ({Status}): {Body}", (int)resp.StatusCode, errBody);
                return null;
            }

            var parsed = await resp.Content.ReadFromJsonAsync<ResponseBody>(cancellationToken: ct);
            var first = parsed?.Translations?.FirstOrDefault();
            if (first is null || string.IsNullOrWhiteSpace(first.Text))
            {
                _log.LogError("DeepL translation returned no text.");
                return null;
            }

            return new TranslationResult(
                TranslatedMarkdown: first.Text,
                SourceLanguageDetected: first.DetectedSourceLanguage ?? request.SourceLanguage ?? "auto",
                Provider: ProviderName);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeepL translation threw.");
            return null;
        }
    }

    /// <summary>
    /// DeepL accepts uppercase ISO codes (EN, ES, FR, DE, ...) plus a few regional variants like
    /// EN-GB, EN-US, PT-BR, PT-PT. We accept ISO-639-1 codes case-insensitively and a curated
    /// set of English-language names so users can type "Spanish" or "es" interchangeably.
    /// Returns null when we can't map.
    /// </summary>
    internal static string? MapToDeepLLanguageCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var key = input.Trim();

        // If it's already a 2-letter or 5-letter code (xx or xx-XX), uppercase and use it.
        if (key.Length is 2 or 5)
        {
            var upper = key.ToUpperInvariant().Replace('_', '-');
            return upper;
        }

        var name = key.ToLowerInvariant();
        return name switch
        {
            "english" => "EN",
            "english (uk)" or "british english" => "EN-GB",
            "english (us)" or "american english" => "EN-US",
            "spanish" or "espanol" or "español" => "ES",
            "french" or "français" or "francais" => "FR",
            "german" or "deutsch" => "DE",
            "portuguese" => "PT-PT",
            "portuguese (brazil)" or "brazilian portuguese" => "PT-BR",
            "italian" or "italiano" => "IT",
            "dutch" or "nederlands" => "NL",
            "polish" or "polski" => "PL",
            "russian" or "русский" => "RU",
            "japanese" or "日本語" => "JA",
            "chinese" or "mandarin" or "mandarin chinese" or "simplified chinese" or "中文" => "ZH",
            "korean" or "한국어" => "KO",
            "turkish" or "türkçe" => "TR",
            "ukrainian" or "українська" => "UK",
            "czech" or "čeština" => "CS",
            "danish" or "dansk" => "DA",
            "swedish" or "svenska" => "SV",
            "norwegian" or "norsk" => "NB",
            "finnish" or "suomi" => "FI",
            "greek" or "ελληνικά" => "EL",
            "romanian" or "română" => "RO",
            "hungarian" or "magyar" => "HU",
            "bulgarian" or "български" => "BG",
            "slovak" or "slovenčina" => "SK",
            "slovenian" or "slovenščina" => "SL",
            "estonian" or "eesti" => "ET",
            "latvian" or "latviešu" => "LV",
            "lithuanian" or "lietuvių" => "LT",
            "indonesian" or "bahasa indonesia" => "ID",
            "arabic" or "العربية" => "AR",
            _ => null,
        };
    }

    private sealed record ResponseBody(
        [property: JsonPropertyName("translations")] List<TranslationEntry>? Translations);

    private sealed record TranslationEntry(
        [property: JsonPropertyName("detected_source_language")] string? DetectedSourceLanguage,
        [property: JsonPropertyName("text")] string? Text);
}
