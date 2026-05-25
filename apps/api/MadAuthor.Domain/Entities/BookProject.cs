using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class BookProject : IAuditableEntity, ITenantEntity, ISoftDeleteEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? AuthorId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Genre { get; set; }
    public FictionOrNonfiction FictionOrNonfiction { get; set; }
    public string? TargetAudience { get; set; }
    public string? WritingTone { get; set; }
    public string Language { get; set; } = "en";
    public BookProjectStatus Status { get; set; } = BookProjectStatus.Draft;
    public BookProjectWorkflowStage WorkflowStage { get; set; } = BookProjectWorkflowStage.Intake;
    public int CompletionPercentage { get; set; }
    public int? EstimatedPageCount { get; set; }
    public int? EstimatedWordCount { get; set; }
    public int? TargetWordCount { get; set; }
    public string? TargetReadingLevel { get; set; }
    public string? Isbn { get; set; }
    public string? CopyrightText { get; set; }
    public string? PublishingGoal { get; set; }
    /// <summary>
    /// Body-text font face for PDF exports. Free-form string passed straight to QuestPDF's
    /// <c>FontFamily()</c>; null means "use the renderer's default" (Georgia). Only the font
    /// faces installed on the rendering server are guaranteed to work; the UI restricts
    /// selection to a known-installed list (Georgia, Times New Roman, Cambria, Constantia,
    /// Palatino Linotype, Book Antiqua).
    /// </summary>
    public string? BodyFont { get; set; }
    public DateTime? Deadline { get; set; }
    public bool RequireOutlineApproval { get; set; } = true;
    public DateTime? OutlineApprovedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public ICollection<BookChapter> Chapters { get; set; } = new List<BookChapter>();
    public ICollection<BookRequest> Requests { get; set; } = new List<BookRequest>();
    public ICollection<BookCharacter> Characters { get; set; } = new List<BookCharacter>();
    public ICollection<BookAsset> Assets { get; set; } = new List<BookAsset>();
    public ICollection<BookExport> Exports { get; set; } = new List<BookExport>();
    public ICollection<BookCover> Covers { get; set; } = new List<BookCover>();
}
