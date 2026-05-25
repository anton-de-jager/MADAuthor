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
    /// <summary>
    /// PNG of the composed cover (background image + overlaid title/author/subtitle).
    /// Null until the user runs the design step; the raw photo lives in <see cref="AssetId"/>.
    /// Separate column so re-designing doesn't clobber the original background.
    /// </summary>
    public Guid? DesignedAssetId { get; set; }
    public BookCoverStatus Status { get; set; } = BookCoverStatus.Pending;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
