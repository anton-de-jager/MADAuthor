using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class BookRequest : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public BookRequestType RequestType { get; set; }
    public string? IdeaPrompt { get; set; }
    public string? ExistingContent { get; set; }
    public string? Notes { get; set; }
    public string? AIInstructions { get; set; }
    public string? DesiredTone { get; set; }
    public string? DesiredLength { get; set; }
    public string? POVStyle { get; set; }
    public string? WritingStyle { get; set; }
    public string? EndingType { get; set; }
    public string? ThemesCsv { get; set; }
    public string? KeywordsCsv { get; set; }
    public string? EducationalLevel { get; set; }
    public string? CitationStyle { get; set; }

    /// <summary>JSON document of style/fiction/nonfiction/christian/publishing variables. See docs/02-data-model.md §4.</summary>
    public string Variables { get; set; } = "{}";

    /// <summary>JSON document of optional features (workbook, references, marketing, etc.). See docs/02-data-model.md §5.</summary>
    public string Features { get; set; } = "{}";

    public string? TargetPlatformsCsv { get; set; }
    public string? RequestedFormatsCsv { get; set; }
    public byte Priority { get; set; } = 5;
    public BookRequestStatus Status { get; set; } = BookRequestStatus.Submitted;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
