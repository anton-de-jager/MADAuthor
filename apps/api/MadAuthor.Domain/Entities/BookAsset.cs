using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class BookAsset : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public BookAssetType AssetType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public StorageProvider StorageProvider { get; set; } = StorageProvider.Local;
    public string BlobContainer { get; set; } = string.Empty;
    public string BlobKey { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? ChecksumSha256 { get; set; }
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;

    /// <summary>
    /// JSON describing where this asset came from (e.g. Unsplash photographer + URL for attribution).
    /// Shape: { source: "Unsplash" | "User", name?, url?, originalId? }. Null for plain user uploads.
    /// </summary>
    public string? AttributionJson { get; set; }

    /// <summary>
    /// Plain-text content extracted from the file at upload time (for .pdf/.docx/.txt/.md manuscripts).
    /// The Submit endpoint stitches this into <see cref="BookRequest.ExistingContent"/> when a BookRequest
    /// is created, so the worker sees the manuscript via the existing context path.
    /// Null for non-text uploads (images, audio) and for files where extraction failed.
    /// </summary>
    public string? ExtractedText { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
