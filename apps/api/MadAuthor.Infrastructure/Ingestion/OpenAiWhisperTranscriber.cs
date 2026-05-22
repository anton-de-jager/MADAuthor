using System.Net.Http.Headers;
using System.Text.Json;
using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Ingestion;

/// <summary>
/// OpenAI Whisper transcription client. Gated on <c>OPENAI_API_KEY</c> (the DI extension only
/// registers this implementation when the key is non-empty). Calls
/// <c>POST https://api.openai.com/v1/audio/transcriptions</c> with <c>multipart/form-data</c>,
/// using <c>response_format=verbose_json</c> so we can also surface the detected language.
/// </summary>
public sealed class OpenAiWhisperTranscriber : IAudioTranscriber
{
    // Whisper API hard cap. Files larger than this fail server-side; we surface a clean null +
    // a warning instead of paying for a guaranteed-failed request. Chunking is out of scope.
    private const long MaxBytes = 25L * 1024 * 1024;
    private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
    private const string Model = "whisper-1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly OpenAiMediaOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiWhisperTranscriber> _log;

    public OpenAiWhisperTranscriber(
        OpenAiMediaOptions options,
        IHttpClientFactory httpFactory,
        ILogger<OpenAiWhisperTranscriber> log)
    {
        _options = options;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ProviderName => "openai-whisper-1";

    public async Task<AudioTranscription?> TranscribeAsync(
        Stream audio, string fileName, string? mimeType, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        // Buffer to a known size — multipart needs a Content-Length and Whisper rejects > 25 MB.
        // For typical voice notes (a few MB) this is cheap; large files are bounded by the API
        // controller's 50 MB upload cap anyway.
        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await audio.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        if (bytes.LongLength > MaxBytes)
        {
            _log.LogWarning(
                "Whisper transcription skipped: {File} is {Size} bytes, exceeds {Max} byte API cap.",
                fileName, bytes.LongLength, MaxBytes);
            return null;
        }

        if (bytes.Length == 0)
        {
            _log.LogWarning("Whisper transcription skipped: {File} is empty.", fileName);
            return null;
        }

        using var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        // Transcription can legitimately take a while for longer clips; the request-side cap is
        // generous so a slow voice note doesn't get truncated by a 100s default.
        http.Timeout = TimeSpan.FromMinutes(5);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        // Whisper sniffs by extension, so make sure we send a sane filename even if the upload
        // didn't ship one (rare but possible).
        var sendName = string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName;
        form.Add(fileContent, "file", sendName);
        form.Add(new StringContent(Model), "model");
        form.Add(new StringContent("verbose_json"), "response_format");

        try
        {
            using var resp = await http.PostAsync(Endpoint, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                _log.LogWarning(
                    "Whisper API returned {Status} for {File}: {Body}",
                    (int)resp.StatusCode, fileName, body);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<WhisperResponse>(stream, JsonOpts, ct);
            var text = payload?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _log.LogInformation("Whisper returned no text for {File}.", fileName);
                return null;
            }

            return new AudioTranscription(text, payload?.Language, ProviderName);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Whisper transcription failed for {File}.", fileName);
            return null;
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return "<unreadable>"; }
    }

    private sealed record WhisperResponse(string? Text, string? Language);
}
