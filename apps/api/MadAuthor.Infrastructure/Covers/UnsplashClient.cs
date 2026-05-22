using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MadAuthor.Application.Covers;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Covers;

public class UnsplashOptions
{
    public string? AccessKey { get; set; }
    public string AppName { get; set; } = "MADAuthor";
}

/// <summary>
/// Minimal Unsplash REST client. Honors the Unsplash API guidelines:
///  - Uses the documented <c>links.download_location</c> tracking endpoint when fetching photo bytes.
///  - Returns the full photo record so attribution (photographer name + profile URL) can be stored.
/// Requires a free Access Key from https://unsplash.com/developers — read from <c>UNSPLASH_ACCESS_KEY</c>.
/// </summary>
public class UnsplashClient : IUnsplashClient
{
    private static readonly Uri BaseUri = new("https://api.unsplash.com/");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly UnsplashOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<UnsplashClient> _log;

    public UnsplashClient(UnsplashOptions options, HttpClient http, ILogger<UnsplashClient> log)
    {
        _options = options;
        _http = http;
        _log = log;

        _http.BaseAddress ??= BaseUri;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd($"{_options.AppName}/1.0");
        if (!string.IsNullOrWhiteSpace(_options.AccessKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Client-ID", _options.AccessKey);
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.AccessKey);

    public async Task<IReadOnlyList<UnsplashPhoto>> SearchAsync(
        string query, int perPage = 12, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("UNSPLASH_ACCESS_KEY is not configured.");
        var capped = Math.Clamp(perPage, 1, 30);
        var url = $"search/photos?query={Uri.EscapeDataString(query)}&per_page={capped}&orientation=portrait";
        var raw = await _http.GetFromJsonAsync<SearchResponse>(url, JsonOpts, ct);
        return raw?.Results ?? new List<UnsplashPhoto>();
    }

    public async Task<UnsplashPhoto?> GetAsync(string id, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("UNSPLASH_ACCESS_KEY is not configured.");
        try
        {
            return await _http.GetFromJsonAsync<UnsplashPhoto>($"photos/{id}", JsonOpts, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<(byte[] Bytes, string ContentType)> DownloadAsync(
        string downloadLocation, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("UNSPLASH_ACCESS_KEY is not configured.");

        // 1. Hit Unsplash's tracking endpoint as required by their API guidelines.
        try
        {
            var tracking = await _http.GetFromJsonAsync<DownloadTrackingResponse>(
                downloadLocation, JsonOpts, ct);
            if (tracking?.Url is null)
                throw new InvalidOperationException("Unsplash did not return a download URL.");

            // 2. Fetch the actual image bytes — Unsplash CDN doesn't require auth headers.
            using var cdn = new HttpClient();
            cdn.DefaultRequestHeaders.UserAgent.TryParseAdd($"{_options.AppName}/1.0");
            using var resp = await cdn.GetAsync(tracking.Url, ct);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return (bytes, contentType);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unsplash download failed for {Location}", downloadLocation);
            throw;
        }
    }

    private record SearchResponse(int Total, int TotalPages, List<UnsplashPhoto>? Results);
    private record DownloadTrackingResponse(string? Url);
}
