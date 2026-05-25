using MadAuthor.Domain.Enums;

namespace MadAuthor.Contracts.Books;

public record CreateBookRequest(
    string Title,
    string? Subtitle,
    string? Genre,
    FictionOrNonfiction FictionOrNonfiction,
    string? TargetAudience,
    string? WritingTone,
    string Language,
    int? TargetWordCount,
    string? TargetReadingLevel);

public record BookSummary(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Genre,
    BookProjectStatus Status,
    BookProjectWorkflowStage WorkflowStage,
    int CompletionPercentage,
    DateTime CreatedDate);

public record BookDetail(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Description,
    string? Genre,
    FictionOrNonfiction FictionOrNonfiction,
    string? TargetAudience,
    string? WritingTone,
    string Language,
    BookProjectStatus Status,
    BookProjectWorkflowStage WorkflowStage,
    int CompletionPercentage,
    int? TargetWordCount,
    string? TargetReadingLevel,
    bool RequireOutlineApproval,
    DateTime? OutlineApprovedAt,
    DateTime CreatedDate,
    Guid? AuthorId,
    string? AuthorPenName,
    string? BodyFont,
    IReadOnlyList<BookChapterSummary> Chapters);

public record BookChapterSummary(
    Guid Id,
    int ChapterNumber,
    string Title,
    string? Summary,
    int WordCount,
    BookChapterStatus Status);

public record OutlineChapter(
    Guid? Id,
    int ChapterNumber,
    string Title,
    string? Summary);

public record OutlineUpdateRequest(
    IReadOnlyList<OutlineChapter> Chapters);

/// <summary>
/// Partial update. Only non-null fields are written. The front-end sends only the
/// fields the user actually changed (e.g. <c>{ "title": "New title" }</c>).
/// </summary>
public record UpdateBookRequest(
    string? Title,
    string? Subtitle,
    string? Genre,
    FictionOrNonfiction? FictionOrNonfiction,
    string? TargetAudience,
    string? WritingTone,
    string? Language,
    int? TargetWordCount,
    string? TargetReadingLevel,
    Guid? AuthorId,
    string? BodyFont);

public record AuthorSummary(
    Guid Id,
    string PenName);

public record CreateAuthorRequest(
    string PenName,
    string? Biography);

public record UpdateAuthorRequest(
    string? PenName,
    string? Biography);

public record SubmitBookRequest(
    BookRequestType RequestType,
    string? IdeaPrompt,
    string? ExistingContent,
    string? Notes,
    string? AIInstructions,
    string? DesiredTone,
    string? DesiredLength,
    string? POVStyle,
    string? WritingStyle,
    string? ThemesCsv,
    string? KeywordsCsv,
    object? Variables,
    object? Features,
    string? TargetPlatformsCsv,
    string? RequestedFormatsCsv,
    byte Priority);
