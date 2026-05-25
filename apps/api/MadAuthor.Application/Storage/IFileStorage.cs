namespace MadAuthor.Application.Storage;

public interface IFileStorage
{
    /// <summary>Save a stream. Returns the storage key the file can later be opened with.</summary>
    Task<string> SaveAsync(string container, string keyHint, Stream content, CancellationToken ct = default);

    /// <summary>Open a previously saved file for reading.</summary>
    Stream OpenRead(string container, string key);

    /// <summary>Delete by key. Idempotent.</summary>
    Task DeleteAsync(string container, string key, CancellationToken ct = default);

    /// <summary>Return the absolute path/URI of a stored object. For local storage, the absolute path.</summary>
    string ResolvePath(string container, string key);
}
