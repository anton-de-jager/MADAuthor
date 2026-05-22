namespace MadAuthor.Worker;

/// <summary>JSON shape the Planner subagent must produce. Consumed by `write-planning`.</summary>
public record PlannerOutput(
    string? NarrativeArc,
    List<string>? Themes,
    int? EstimatedWordCount,
    int? EstimatedPageCount,
    List<PlannerChapter> Chapters,
    List<PlannerCharacter>? Characters,
    List<string>? ResearchTopics);

public record PlannerChapter(
    int Number,
    string? Title,
    string? Summary,
    int? TargetWordCount);

public record PlannerCharacter(
    string Name,
    string? Description,
    string? Personality,
    string? Background,
    string? Goals,
    string? Conflicts);

public record ContinuityReport(
    bool IssuesFound,
    List<int>? ChaptersNeedingRevision);

public record PublisherOutput(
    string? ShortDescription,
    string? KdpDescription,
    string[]? Keywords,
    string[]? BisacCodes,
    string[]? SuggestedCategories,
    string? RefinedSubtitle,
    string? IsbnPageText,
    string? CopyrightText,
    string? Acknowledgements,
    string? Dedication,
    string? AuthorBio);
