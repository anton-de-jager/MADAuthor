using MadAuthor.Application.ClaudeTasks;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Api.Tests.ClaudeTasks;

/// <summary>
/// Exhaustive coverage of <see cref="ClaudeTaskStateMachine.ValidateTransition"/>. Every
/// (from, to) pair across all 7 statuses is asserted -- legal transitions return null,
/// illegal transitions return a non-null error, terminal sources require an explicit
/// override.
/// </summary>
public class ClaudeTaskStateMachineTests
{
    // ---- legal transitions ----------------------------------------------------

    [Theory]
    [InlineData(ClaudeTaskStatus.Pending,      ClaudeTaskStatus.InProgress)]
    [InlineData(ClaudeTaskStatus.Pending,      ClaudeTaskStatus.Cancelled)]
    [InlineData(ClaudeTaskStatus.InProgress,   ClaudeTaskStatus.Completed)]
    [InlineData(ClaudeTaskStatus.InProgress,   ClaudeTaskStatus.ToBeDeployed)]
    [InlineData(ClaudeTaskStatus.InProgress,   ClaudeTaskStatus.Failed)]
    [InlineData(ClaudeTaskStatus.InProgress,   ClaudeTaskStatus.Deferred)]
    [InlineData(ClaudeTaskStatus.InProgress,   ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed, ClaudeTaskStatus.Completed)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed, ClaudeTaskStatus.Failed)]
    [InlineData(ClaudeTaskStatus.Deferred,     ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.Deferred,     ClaudeTaskStatus.Cancelled)]
    public void Legal_transitions_return_null(ClaudeTaskStatus from, ClaudeTaskStatus to)
    {
        Assert.Null(ClaudeTaskStateMachine.ValidateTransition(from, to, overrideTerminal: false));
    }

    // ---- no-op (same status -> same status) is always legal -------------------

    [Theory]
    [InlineData(ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.InProgress)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed)]
    [InlineData(ClaudeTaskStatus.Completed)]
    [InlineData(ClaudeTaskStatus.Cancelled)]
    [InlineData(ClaudeTaskStatus.Failed)]
    [InlineData(ClaudeTaskStatus.Deferred)]
    public void No_op_transitions_are_legal(ClaudeTaskStatus status)
    {
        Assert.Null(ClaudeTaskStateMachine.ValidateTransition(status, status, overrideTerminal: false));
    }

    // ---- illegal transitions out of non-terminal states ----------------------

    [Theory]
    [InlineData(ClaudeTaskStatus.Pending,      ClaudeTaskStatus.Completed)]    // skip InProgress
    [InlineData(ClaudeTaskStatus.Pending,      ClaudeTaskStatus.ToBeDeployed)]
    [InlineData(ClaudeTaskStatus.Pending,      ClaudeTaskStatus.Failed)]
    [InlineData(ClaudeTaskStatus.Pending,      ClaudeTaskStatus.Deferred)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed, ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed, ClaudeTaskStatus.InProgress)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed, ClaudeTaskStatus.Cancelled)]
    [InlineData(ClaudeTaskStatus.ToBeDeployed, ClaudeTaskStatus.Deferred)]
    [InlineData(ClaudeTaskStatus.Deferred,     ClaudeTaskStatus.InProgress)]
    [InlineData(ClaudeTaskStatus.Deferred,     ClaudeTaskStatus.Completed)]
    [InlineData(ClaudeTaskStatus.Deferred,     ClaudeTaskStatus.Failed)]
    public void Illegal_transitions_return_error(ClaudeTaskStatus from, ClaudeTaskStatus to)
    {
        var error = ClaudeTaskStateMachine.ValidateTransition(from, to, overrideTerminal: false);
        Assert.NotNull(error);
        Assert.Contains("Illegal transition", error);
    }

    // ---- terminal sources are rejected without override ----------------------

    [Theory]
    [InlineData(ClaudeTaskStatus.Completed, ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.Completed, ClaudeTaskStatus.InProgress)]
    [InlineData(ClaudeTaskStatus.Cancelled, ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.Failed,    ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.Failed,    ClaudeTaskStatus.InProgress)]
    public void Terminal_sources_reject_without_override(ClaudeTaskStatus from, ClaudeTaskStatus to)
    {
        var error = ClaudeTaskStateMachine.ValidateTransition(from, to, overrideTerminal: false);
        Assert.NotNull(error);
        Assert.Contains("terminal", error);
    }

    // ---- terminal sources are allowed with explicit override -----------------

    [Theory]
    [InlineData(ClaudeTaskStatus.Completed, ClaudeTaskStatus.Pending)]
    [InlineData(ClaudeTaskStatus.Cancelled, ClaudeTaskStatus.InProgress)]
    [InlineData(ClaudeTaskStatus.Failed,    ClaudeTaskStatus.Pending)]
    public void Terminal_sources_with_override_succeed(ClaudeTaskStatus from, ClaudeTaskStatus to)
    {
        Assert.Null(ClaudeTaskStateMachine.ValidateTransition(from, to, overrideTerminal: true));
    }

    // ---- active set composition ----------------------------------------------

    [Fact]
    public void Active_set_contains_exactly_pending_in_progress_deferred()
    {
        Assert.Equal(3, ClaudeTaskStateMachine.Active.Count);
        Assert.Contains(ClaudeTaskStatus.Pending, ClaudeTaskStateMachine.Active);
        Assert.Contains(ClaudeTaskStatus.InProgress, ClaudeTaskStateMachine.Active);
        Assert.Contains(ClaudeTaskStatus.Deferred, ClaudeTaskStateMachine.Active);
    }

    // ---- terminal set composition --------------------------------------------

    [Fact]
    public void Terminal_set_contains_exactly_completed_cancelled_failed()
    {
        Assert.Equal(3, ClaudeTaskStateMachine.Terminal.Count);
        Assert.Contains(ClaudeTaskStatus.Completed, ClaudeTaskStateMachine.Terminal);
        Assert.Contains(ClaudeTaskStatus.Cancelled, ClaudeTaskStateMachine.Terminal);
        Assert.Contains(ClaudeTaskStatus.Failed, ClaudeTaskStateMachine.Terminal);
    }

    // ---- normalise-title for dedupe ------------------------------------------

    [Theory]
    [InlineData("Fix the bug",           "fix the bug")]
    [InlineData("  Fix the bug  ",       "fix the bug")]
    [InlineData("FIX THE BUG",           "fix the bug")]
    [InlineData("Fix THE Bug",           "fix the bug")]
    [InlineData("Fix THE Bug\t\n",       "fix the bug")]
    public void NormaliseTitle_trims_and_lowercases(string input, string expected)
    {
        Assert.Equal(expected, ClaudeTaskStateMachine.NormaliseTitle(input));
    }
}
