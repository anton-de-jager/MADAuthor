using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class BookExport : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public BookExportType ExportType { get; set; }
    public string? BlobKey { get; set; }
    public long? FileSize { get; set; }
    public string? ChecksumSha256 { get; set; }
    public BookExportStatus Status { get; set; } = BookExportStatus.Queued;
    public string? ErrorMessage { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int DownloadCount { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
