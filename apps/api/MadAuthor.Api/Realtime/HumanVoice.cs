using MadAuthor.Domain.Enums;

namespace MadAuthor.Api.Realtime;

/// <summary>
/// Translates internal job/worker vocabulary into user-facing language that sounds like
/// a human team is working on the book. Everything pushed over SignalR or rendered in
/// the SPA goes through here so internal strings (exceptions, diagnostic messages, raw
/// stage codes) never reach the user.
///
/// Rules of thumb:
///   - Speak in present continuous ("Sketching..."), not past tense or imperative.
///   - Name roles ("the Planner", "the Writer", "the Editor") rather than systems.
///   - Errors become "we hit a small snag" + a retry hint, never a stack-class name.
///   - When in doubt, omit. Silence is better than leaking jargon.
/// </summary>
public static class HumanVoice
{
    /// <summary>
    /// Maps a raw <c>AIJobQueue.ErrorMessage</c> into something a user can read without
    /// breaking the illusion. Returns null if there is no error worth surfacing.
    /// </summary>
    public static string? HumanizeError(string? raw, AIJobType jobType)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Hide any diagnostic / system-internal release notes outright - these come
        // from the worker's `release` subcommand or developer-side test claims.
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("diagnostic")
            || lower.Contains("releasing")
            || lower.Contains("sanity")
            || lower.Contains("test claim"))
        {
            return null;
        }

        // Anything that smells like a stack trace, JSON parse error, or SQL message
        // gets a polite, role-named generic.
        if (lower.Contains("exception")
            || lower.Contains("sqlexception")
            || lower.Contains("json")
            || lower.Contains("invalid object")
            || lower.Contains("0x")
            || lower.Contains("\\u00")
            || lower.Contains("null reference"))
        {
            return jobType switch
            {
                AIJobType.PlanBook        => "Our planner hit a small snag and is starting again.",
                AIJobType.DraftChapter    => "Our writer hit a small snag on this chapter and is having another go.",
                AIJobType.EditChapter     => "Our editor needs another pass - circling back now.",
                AIJobType.ResearchTopic   => "The research desk needs another moment to confirm sources.",
                AIJobType.ContinuityCheck => "Our continuity reader is double-checking a few details.",
                AIJobType.GenerateCover   => "The cover designer is trying a fresh angle.",
                AIJobType.GenerateMetadata=> "Our editor is finalising the book metadata.",
                AIJobType.GenerateMarketing => "The marketing desk is reworking the blurb.",
                _ => "We hit a small snag and are trying again.",
            };
        }

        // Otherwise the message was already authored as user-facing prose; pass through.
        return raw;
    }

    /// <summary>
    /// Maps a raw <c>Stage</c> string set by the worker (e.g. "Planning", "Drafting")
    /// to a user-facing present-continuous phrase.
    /// </summary>
    public static string? HumanizeStage(string? rawStage, AIJobType jobType)
    {
        if (string.IsNullOrWhiteSpace(rawStage)) return DefaultStageFor(jobType);

        return rawStage.Trim().ToLowerInvariant() switch
        {
            "planning"   => "Our planner is sketching the chapter outline.",
            "drafting"   => "Our writer is drafting this chapter now.",
            "editing"    => "Our editor is polishing this chapter.",
            "researching"=> "The research desk is pulling supporting material.",
            "continuity" => "Our continuity reader is checking the flow.",
            "metadata"   => "Our editor is finalising the book's metadata.",
            "marketing"  => "The marketing desk is shaping the book's blurb.",
            "cover"      => "The cover designer is at work.",
            _ => DefaultStageFor(jobType),
        };
    }

    /// <summary>
    /// User-facing status label. Hides "Claimed" entirely - to the user, "Claimed" and
    /// "InProgress" both just mean "someone on the team is on it right now".
    /// </summary>
    public static string HumanizeStatus(AIJobStatus s) => s switch
    {
        AIJobStatus.Pending    => "Queued",
        AIJobStatus.Claimed    => "In progress",
        AIJobStatus.InProgress => "In progress",
        AIJobStatus.Completed  => "Done",
        AIJobStatus.Failed     => "Paused",   // user-visible "we ran into trouble" without alarming
        _ => s.ToString(),
    };

    private static string DefaultStageFor(AIJobType jobType) => jobType switch
    {
        AIJobType.PlanBook        => $"{Persona(jobType)} is mapping out the book.",
        AIJobType.DraftChapter    => $"{Persona(jobType)} is on this chapter.",
        AIJobType.EditChapter     => $"{Persona(jobType)} is reviewing this chapter.",
        AIJobType.ResearchTopic   => $"{Persona(jobType)} is digging up references.",
        AIJobType.ContinuityCheck => $"{Persona(jobType)} is checking the manuscript.",
        AIJobType.GenerateMetadata=> $"{Persona(jobType)} is finalising the book metadata.",
        AIJobType.GenerateMarketing => $"{Persona(jobType)} is preparing the blurb.",
        AIJobType.GenerateCover   => $"{Persona(jobType)} is at work on the cover.",
        _ => "The team is working on it.",
    };

    /// <summary>
    /// Each role on the AI book team gets a name. Static for now - one persona per role -
    /// so the user sees consistent "people" across the lifecycle of a book.
    /// </summary>
    public static string Persona(AIJobType jobType) => jobType switch
    {
        AIJobType.PlanBook          => "Maya the planner",
        AIJobType.DraftChapter      => "Sipho the writer",
        AIJobType.EditChapter       => "Tomas the editor",
        AIJobType.ResearchTopic     => "Lerato in research",
        AIJobType.ContinuityCheck   => "Priya in continuity",
        AIJobType.GenerateMetadata  => "Nora the publisher",
        AIJobType.GenerateMarketing => "Daniel in marketing",
        AIJobType.GenerateCover     => "Aiden the cover designer",
        _ => "the team",
    };

    /// <summary>
    /// Produce a short, human, persona-flavoured toast string for a freshly-completed job.
    /// Returns null when the job type doesn't warrant a per-event toast (or when the
    /// caller didn't supply the chapter context that would make the toast meaningful).
    /// </summary>
    public static string? BuildMilestoneToast(
        AIJobType jobType,
        string? bookTitle = null,
        int? chapterNumber = null,
        string? chapterTitle = null)
    {
        var book = string.IsNullOrWhiteSpace(bookTitle) ? "your book" : $"“{bookTitle.Trim()}”";
        var chapterTag = (chapterNumber, chapterTitle) switch
        {
            (int n, string t) when !string.IsNullOrWhiteSpace(t) => $"chapter {n}: {t}",
            (int n, _)                                            => $"chapter {n}",
            _                                                      => "this chapter",
        };

        return jobType switch
        {
            AIJobType.PlanBook =>
                $"Maya just finished sketching the chapter outline for {book}.",
            AIJobType.DraftChapter =>
                $"Sipho freshly drafted {chapterTag}.",
            AIJobType.EditChapter =>
                $"Tomas polished {chapterTag}.",
            AIJobType.ResearchTopic =>
                "Lerato wrapped up a research pass.",
            AIJobType.ContinuityCheck =>
                "Priya finished a continuity review.",
            AIJobType.GenerateMetadata =>
                "Nora locked in the book's metadata.",
            AIJobType.GenerateMarketing =>
                "Daniel drafted the marketing kit.",
            AIJobType.GenerateCover =>
                "Aiden delivered a cover concept.",
            _ => null,
        };
    }
}
