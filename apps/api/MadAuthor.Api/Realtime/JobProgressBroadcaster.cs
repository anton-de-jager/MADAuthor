using System.Text.Json;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Realtime;

/// <summary>
/// Background loop that polls AIJobQueue for recent updates and broadcasts them to
/// SignalR groups <c>project:{BookProjectId}</c>. This is the only server-side polling
/// left in the pipeline. It's invisible to the client (the client sees pure WebSocket
/// pushes), but a future improvement is to have the worker CLI POST an "I updated job X"
/// endpoint after every write so this poll can be retired entirely. See docs/03 §7.
///
/// Poll cadence intentionally generous (10s) - worker writes don't arrive faster than
/// that, and a tighter interval just burns DB IO without benefit.
/// </summary>
public class JobProgressBroadcaster(
    IServiceScopeFactory scopes,
    ILogger<JobProgressBroadcaster> log) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private DateTime _lastTick = DateTime.UtcNow.AddMinutes(-5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Avoid starting until the app is fully up - gives EF migrations and SignalR
        // a moment to initialize before our first poll.
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "JobProgressBroadcaster tick failed; will retry.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task TickOnce(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MadAuthorDbContext>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

        var since = _lastTick;
        var updates = await db.AIJobQueue
            .AsNoTracking()
            .Where(j => j.UpdatedDate != null
                        && j.UpdatedDate > since
                        && (j.Status == AIJobStatus.Claimed
                            || j.Status == AIJobStatus.InProgress
                            || j.Status == AIJobStatus.Completed
                            || j.Status == AIJobStatus.Failed))
            .OrderBy(j => j.UpdatedDate)
            .Select(j => new
            {
                j.Id,
                j.BookProjectId,
                JobType = j.JobType.ToString(),
                Status = j.Status.ToString(),
                j.Stage,
                j.Progress,
                j.ErrorMessage,
                j.UpdatedDate,
                j.InputJson,
            })
            .ToListAsync(ct);

        if (updates.Count == 0) return;

        foreach (var u in updates)
        {
            // Every field that reaches the SPA goes through HumanVoice first. The internal
            // status/stage/error strings are useful for ops + this server's logs, but the
            // user should never see "Diagnostic claim only" or "SqlException 0xEF...".
            var jobTypeEnum = Enum.Parse<AIJobType>(u.JobType);
            var statusEnum  = Enum.Parse<AIJobStatus>(u.Status);

            // Build a milestone toast for jobs that just landed in Completed state.
            // Look up book title and (for chapter-scoped jobs) chapter info so the
            // toast can name what was finished - "Sipho freshly drafted chapter 4: Men Who Took a Chance".
            string? toast = null;
            if (statusEnum == AIJobStatus.Completed)
            {
                var bookTitle = await db.BookProjects
                    .Where(p => p.Id == u.BookProjectId)
                    .Select(p => p.Title)
                    .FirstOrDefaultAsync(ct);

                int? chNum = null;
                string? chTitle = null;
                if (jobTypeEnum == AIJobType.DraftChapter || jobTypeEnum == AIJobType.EditChapter)
                {
                    var chapterId = TryReadChapterId(u.InputJson);
                    if (chapterId is { } cid)
                    {
                        var ch = await db.BookChapters
                            .Where(c => c.Id == cid)
                            .Select(c => new { c.ChapterNumber, c.Title })
                            .FirstOrDefaultAsync(ct);
                        if (ch != null)
                        {
                            chNum = ch.ChapterNumber;
                            chTitle = ch.Title;
                        }
                    }
                }
                toast = HumanVoice.BuildMilestoneToast(jobTypeEnum, bookTitle, chNum, chTitle);
            }

            await hub.Clients
                .Group($"project:{u.BookProjectId}")
                .SendAsync("JobProgress", new
                {
                    jobId = u.Id,
                    bookProjectId = u.BookProjectId,
                    jobType = u.JobType,
                    status = HumanVoice.HumanizeStatus(statusEnum),
                    stage = HumanVoice.HumanizeStage(u.Stage, jobTypeEnum),
                    progress = u.Progress,
                    errorMessage = HumanVoice.HumanizeError(u.ErrorMessage, jobTypeEnum),
                    milestoneToast = toast,
                }, ct);
        }

        _lastTick = updates[^1].UpdatedDate ?? DateTime.UtcNow;
        log.LogDebug("Broadcast {Count} job-progress events (lastTick={Tick:O})",
            updates.Count, _lastTick);
    }

    /// <summary>Best-effort extract of "chapterId" from a job's InputJson; returns null on any parse failure.</summary>
    private static Guid? TryReadChapterId(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.TryGetProperty("chapterId", out var el)
                && el.ValueKind == JsonValueKind.String
                && Guid.TryParse(el.GetString(), out var id))
            {
                return id;
            }
        }
        catch { /* swallow - best effort */ }
        return null;
    }
}
