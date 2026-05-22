namespace MadAuthor.Application.Covers;

public interface IUnsplashClient
{
    Task<IReadOnlyList<UnsplashPhoto>> SearchAsync(string query, int perPage = 12, CancellationToken ct = default);
    Task<UnsplashPhoto?> GetAsync(string id, CancellationToken ct = default);
    Task<(byte[] Bytes, string ContentType)> DownloadAsync(string downloadLocation, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public record UnsplashPhoto(
    string Id,
    int Width,
    int Height,
    string? Color,
    string? Description,
    string? AltDescription,
    UnsplashUrls Urls,
    UnsplashLinks Links,
    UnsplashUser User);

public record UnsplashUrls(string Raw, string Full, string Regular, string Small, string Thumb);
public record UnsplashLinks(string Self, string Html, string Download, string DownloadLocation);
public record UnsplashUser(string Id, string Username, string Name, UnsplashUserLinks Links);
public record UnsplashUserLinks(string Self, string Html);
