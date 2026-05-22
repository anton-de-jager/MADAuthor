using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class BookCover : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? Style { get; set; }
    public Guid? AssetId { get; set; }
    public BookCoverStatus Status { get; set; } = BookCoverStatus.Pending;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
