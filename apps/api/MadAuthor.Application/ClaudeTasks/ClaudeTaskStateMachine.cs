using MadAuthor.Domain.Enums;

namespace MadAuthor.Application.ClaudeTasks;

/// <summary>
/// State-machine validator for <see cref="ClaudeTaskStatus"/> transitions. Lifted out of
/// the controller so unit tests can exercise it without a DbContext. The controller calls
/// <see cref="ValidateTransition"/> from its PATCH handler.
/// </summary>
/// <remarks>
/// Rules (matches docs/08-claude-task-system.md section 2):
/// <list type="bullet">
///   <item>Pending -> InProgress, Cancelled</item>
///   <item>InProgress -> Completed, ToBeDeployed, Failed, Deferred, Pending</item>
///   <item>ToBeDeployed -> Completed, Failed</item>
///   <item>Deferred -> Pending, Cancelled</item>
///   <item>Terminal (Completed, Cancelled, Failed) -> blocked unless <c>overrideTerminal=true</c></item>
/// </list>
/// </remarks>
public static class ClaudeTaskStateMachine
{
    /// <summary>The three statuses that <see cref="ClaudeTaskStatus.Failed"/>'s peers --
    /// they reject mutations without an explicit override.</summary>
    public static readonly IReadOnlyList<ClaudeTaskStatus> Terminal = new[]
    {
        ClaudeTaskStatus.Completed,
        ClaudeTaskStatus.Cancelled,
        ClaudeTaskStatus.Failed,
    };

    /// <summary>The statuses <c>findNext()</c> returns -- the active polling bucket.</summary>
    public static readonly IReadOnlyList<ClaudeTaskStatus> Active = new[]
    {
        ClaudeTaskStatus.Pending,
        ClaudeTaskStatus.InProgress,
        ClaudeTaskStatus.Deferred,
    };

    /// <summary>
    /// Returns null on a permitted transition, otherwise an explanatory error message.
    /// </summary>
    public static string? ValidateTransition(ClaudeTaskStatus from, ClaudeTaskStatus to, bool overrideTerminal)
    {
        if (from == to) return null;

        if (Terminal.Contains(from))
        {
            return overrideTerminal
                ? null
                : $"Cannot transition out of terminal status {from}. Pass ?override=true to force.";
        }

        return from switch
        {
            ClaudeTaskStatus.Pending when to is ClaudeTaskStatus.InProgress
                                            or ClaudeTaskStatus.Cancelled => null,

            ClaudeTaskStatus.InProgress when to is ClaudeTaskStatus.Completed
                                              or ClaudeTaskStatus.ToBeDeployed
                                              or ClaudeTaskStatus.Failed
                                              or ClaudeTaskStatus.Deferred
                                              or ClaudeTaskStatus.Pending => null,

            ClaudeTaskStatus.ToBeDeployed when to is ClaudeTaskStatus.Completed
                                                 or ClaudeTaskStatus.Failed => null,

            ClaudeTaskStatus.Deferred when to is ClaudeTaskStatus.Pending
                                             or ClaudeTaskStatus.Cancelled => null,

            _ => $"Illegal transition: {from} -> {to}.",
        };
    }

    /// <summary>
    /// Normalises a title for dedupe: trim + lowercase invariant. Used by
    /// <c>POST /api/claude-tasks/import-bulk</c> and by tests.
    /// </summary>
    public static string NormaliseTitle(string title) => title.Trim().ToLowerInvariant();
}
