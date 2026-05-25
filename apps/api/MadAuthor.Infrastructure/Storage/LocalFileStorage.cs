using MadAuthor.Application.Storage;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Storage;

public class LocalFileStorageOptions
{
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Local-disk implementation of <see cref="IFileStorage"/>. Phase 1 only - swap for an
/// <c>AzureBlobFileStorage</c> or <c>S3FileStorage</c> in Phase 4+ without changing callers.
/// </summary>
public class LocalFileStorage(LocalFileStorageOptions options, ILogger<LocalFileStorage> log) : IFileStorage
{
    public async Task<string> SaveAsync(string container, string keyHint, Stream content, CancellationToken ct = default)
    {
        var safeHint = SafePath(keyHint);
        var fullPath = Path.Combine(options.RootPath, container, safeHint);
        // keyHint includes nested subkeys ({companyId}/{projectId}/{assetId}-{filename}),
        // so create the full parent chain - not just the container root.
        var parentDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);
        await using (var fs = File.Create(fullPath))
        {
            await content.CopyToAsync(fs, ct);
        }
        log.LogDebug("Saved blob {Container}/{Key} ({Bytes} bytes)", container, safeHint, new FileInfo(fullPath).Length);
        return safeHint;
    }

    public Stream OpenRead(string container, string key)
    {
        var path = Path.Combine(options.RootPath, container, SafePath(key));
        return File.OpenRead(path);
    }

    public Task DeleteAsync(string container, string key, CancellationToken ct = default)
    {
        var path = Path.Combine(options.RootPath, container, SafePath(key));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string ResolvePath(string container, string key) =>
        Path.Combine(options.RootPath, container, SafePath(key));

    private static string SafePath(string raw)
    {
        // Reject path traversal. Keep slashes for nested subkeys.
        if (raw.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"Storage key may not contain '..': {raw}");
        return raw.Replace('\\', '/');
    }
}
