using MadAuthor.Domain.Common;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Domain.Entities;

public class BookChapter : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid BookProjectId { get; set; }
    public int ChapterNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ContentMarkdown { get; set; }
    public string? ContentHtml { get; set; }
    public int WordCount { get; set; }
    public BookChapterStatus Status { get; set; } = BookChapterStatus.Planned;
    public Guid? GeneratedByJobId { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }

    public BookProject? BookProject { get; set; }
}
