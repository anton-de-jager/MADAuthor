using System.Text.Json;
using MadAuthor.Application.Email;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Identity;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Realtime;

/// <summary>
/// Watches AIJobQueue for Completed jobs and enqueues the next pipeline stage:
///   PlanBook → DraftChapter × N → EditChapter × N → ContinuityCheck → GenerateMetadata → GenerateMarketing
///
/// Orchestration is stateless and idempotent — every "enqueue follow-up" call first checks
/// whether the follow-up job already exists for that project/chapter, so a restart that
/// re-scans recent completed jobs won't duplicate work.
///
/// See docs/04-ai-orchestration.md.
/// </summary>
public class PipelineOrchestrator(
    IServiceScopeFactory scopes,
    ILogger<PipelineOrchestrator> log) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(2);

    // Widened from 10min to 24h so an API restart after extended downtime still
    // sees recent completions. Every follow-up handler is idempotent (AnyAsync
    // guards on existing rows), so re-processing the past day is harmless.
    private DateTime _lastSeenTick = DateTime.UtcNow.AddHours(-24);
    private DateTime _lastReconcileAt = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await Tick(ct); }
            catch (Exception ex) { log.LogWarning(ex, "PipelineOrchestrator tick failed."); }

            // State-based reconciler — heals orchestration gaps that the event-driven
            // Tick can miss (e.g. completions during API downtime, or follow-ups whose
            // SaveChanges silently dropped). Runs at a slower cadence than Tick.
            if (DateTime.UtcNow - _lastReconcileAt >= ReconcileInterval)
            {
                _lastReconcileAt = DateTime.UtcNow;
                try { await ReconcileGaps(ct); }
                catch (Exception ex) { log.LogWarning(ex, "PipelineOrchestrator reconciler failed."); }
            }

            try { await Task.Delay(PollInterval, ct); } catch { return; }
        }
    }

    /// <summary>
    /// Periodic sweep: for every non-terminal project with at least one Completed job,
    /// re-invoke HandleCompleted for each of that project's completions. Every handler
    /// short-circuits if the follow-up it would create already exists, so this is
    /// idempotent and self-healing — gaps caused by downtime or transient failures
    /// get filled within ReconcileInterval.
    /// </summary>
    private async Task ReconcileGaps(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MadAuthorDbContext>();

        var liveProjectIds = await db.BookProjects.IgnoreQueryFilters()
            .Where(p => p.IsDeleted == false
                     && p.Status != BookProjectStatus.Completed
                     && p.Status != BookProjectStatus.Archived)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (liveProjectIds.Count == 0) return;

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var jobs = await db.AIJobQueue
            .Where(j => j.Status == AIJobStatus.Completed
                     && j.CompletedDate != null
                     && j.CompletedDate > cutoff
                     && liveProjectIds.Contains(j.BookProjectId))
            .OrderBy(j => j.CompletedDate)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            try { await HandleCompleted(db, job, ct); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Reconciler: failed to re-handle job {JobId} ({JobType})", job.Id, job.JobType);
            }
        }

        log.LogDebug("Reconciler swept {Jobs} completions across {Projects} live projects.",
            jobs.Count, liveProjectIds.Count);
    }

    private async Task Tick(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MadAuthorDbContext>();

        var since = _lastSeenTick;
        var completed = await db.AIJobQueue
            .Where(j => j.Status == AIJobStatus.Completed && j.CompletedDate != null && j.CompletedDate > since)
            .OrderBy(j => j.CompletedDate)
            .ToListAsync(ct);

        if (completed.Count == 0) return;

        foreach (var job in completed)
        {
            try
            {
                await HandleCompleted(db, job, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to chain follow-up for job {JobId} ({JobType})", job.Id, job.JobType);
            }
        }

        _lastSeenTick = completed[^1].CompletedDate ?? DateTime.UtcNow;
        log.LogDebug("Processed {Count} completed jobs; lastSeenTick={Tick:O}", completed.Count, _lastSeenTick);
    }

    private async Task HandleCompleted(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        switch (job.JobType)
        {
            case AIJobType.PlanBook:           await OnPlanComplete(db, job, ct); break;
            case AIJobType.DraftChapter:       await OnDraftComplete(db, job, ct); break;
            case AIJobType.EditChapter:        await OnEditComplete(db, job, ct); break;
            case AIJobType.ContinuityCheck:    await OnContinuityComplete(db, job, ct); break;
            case AIJobType.GenerateMetadata:   await OnMetadataComplete(db, job, ct); break;
            case AIJobType.GenerateMarketing:  await OnMarketingComplete(db, job, ct); break;
        }
    }

    private async Task OnPlanComplete(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        var project = await db.BookProjects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == job.BookProjectId, ct);
        if (project is null) return;

        if (project.RequireOutlineApproval && project.OutlineApprovedAt is null)
        {
            project.WorkflowStage = BookProjectWorkflowStage.Planning;
            project.UpdatedDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Project {ProjectId} requires outline approval — pausing pipeline.", project.Id);
            return;
        }

        await EnqueueDraftJobsForPlanned(db, job, ct);
    }

    private async Task EnqueueDraftJobsForPlanned(
        MadAuthorDbContext db, AIJobQueueEntry sourceJob, CancellationToken ct)
    {
        var chapters = await db.BookChapters.IgnoreQueryFilters()
            .Where(c => c.BookProjectId == sourceJob.BookProjectId
                        && c.Status == BookChapterStatus.Planned)
            .ToListAsync(ct);

        foreach (var ch in chapters)
        {
            var already = await db.AIJobQueue
                .AnyAsync(j => j.BookProjectId == sourceJob.BookProjectId
                               && j.JobType == AIJobType.DraftChapter
                               && j.InputJson != null
                               && EF.Functions.Like(j.InputJson, $"%\"chapterId\":\"{ch.Id}\"%"), ct);
            if (already) continue;

            db.AIJobQueue.Add(new AIJobQueueEntry
            {
                Id = Guid.NewGuid(),
                BookProjectId = sourceJob.BookProjectId,
                BookRequestId = sourceJob.BookRequestId,
                JobType = AIJobType.DraftChapter,
                Priority = sourceJob.Priority,
                Status = AIJobStatus.Pending,
                InputJson = JsonSerializer.Serialize(new { chapterId = ch.Id, chapterNumber = ch.ChapterNumber }),
                CreatedDate = DateTime.UtcNow,
            });
        }

        var project = await db.BookProjects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == sourceJob.BookProjectId, ct);
        if (project is not null)
        {
            project.WorkflowStage = BookProjectWorkflowStage.Drafting;
            project.UpdatedDate = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Enqueued DraftChapter jobs for {Count} chapters of project {ProjectId}",
            chapters.Count, sourceJob.BookProjectId);
    }

    private async Task OnDraftComplete(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        var chapterId = ExtractChapterId(job.InputJson);
        if (chapterId is null) return;

        var existing = await db.AIJobQueue
            .AnyAsync(j => j.BookProjectId == job.BookProjectId
                           && j.JobType == AIJobType.EditChapter
                           && j.InputJson != null
                           && EF.Functions.Like(j.InputJson, $"%\"chapterId\":\"{chapterId}\"%"), ct);
        if (existing) return;

        db.AIJobQueue.Add(new AIJobQueueEntry
        {
            Id = Guid.NewGuid(),
            BookProjectId = job.BookProjectId,
            BookRequestId = job.BookRequestId,
            JobType = AIJobType.EditChapter,
            Priority = job.Priority,
            Status = AIJobStatus.Pending,
            InputJson = JsonSerializer.Serialize(new { chapterId }),
            CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task OnEditComplete(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        // Are all chapters in Final state? If so, queue ContinuityCheck (once).
        var stats = await db.BookChapters.IgnoreQueryFilters()
            .Where(c => c.BookProjectId == job.BookProjectId)
            .GroupBy(c => c.BookProjectId)
            .Select(g => new
            {
                Total = g.Count(),
                Final = g.Count(c => c.Status == BookChapterStatus.Final),
            })
            .FirstOrDefaultAsync(ct);

        if (stats is null || stats.Total == 0 || stats.Final < stats.Total) return;

        // Don't enqueue if a ContinuityCheck is already pending/running for this project.
        var continuityExists = await db.AIJobQueue
            .AnyAsync(j => j.BookProjectId == job.BookProjectId
                           && j.JobType == AIJobType.ContinuityCheck
                           && j.Status != AIJobStatus.Failed
                           && j.Status != AIJobStatus.Cancelled, ct);
        if (continuityExists) return;

        db.AIJobQueue.Add(new AIJobQueueEntry
        {
            Id = Guid.NewGuid(),
            BookProjectId = job.BookProjectId,
            BookRequestId = job.BookRequestId,
            JobType = AIJobType.ContinuityCheck,
            Priority = job.Priority,
            Status = AIJobStatus.Pending,
            CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task OnContinuityComplete(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        // Per docs/04 §5: cap continuity loop at 2 passes.
        var passes = await db.AIJobQueue.CountAsync(j =>
            j.BookProjectId == job.BookProjectId
            && j.JobType == AIJobType.ContinuityCheck
            && j.Status == AIJobStatus.Completed, ct);

        var chaptersToRevise = TryReadChaptersNeedingRevision(job.OutputJson);

        if (passes >= 2 || chaptersToRevise.Count == 0)
        {
            await EnqueueIfMissing(db, job, AIJobType.GenerateMetadata, ct);
            return;
        }

        // Enqueue Edit jobs for the affected chapters; reset them to Drafted so the
        // Edit dispatch picks the right state. Then enqueue another ContinuityCheck.
        var chapters = await db.BookChapters.IgnoreQueryFilters()
            .Where(c => c.BookProjectId == job.BookProjectId
                        && chaptersToRevise.Contains(c.ChapterNumber))
            .ToListAsync(ct);

        foreach (var ch in chapters)
        {
            ch.Status = BookChapterStatus.Drafted;
            ch.UpdatedDate = DateTime.UtcNow;

            db.AIJobQueue.Add(new AIJobQueueEntry
            {
                Id = Guid.NewGuid(),
                BookProjectId = job.BookProjectId,
                BookRequestId = job.BookRequestId,
                JobType = AIJobType.EditChapter,
                Priority = job.Priority,
                Status = AIJobStatus.Pending,
                InputJson = JsonSerializer.Serialize(new
                {
                    chapterId = ch.Id,
                    chapterNumber = ch.ChapterNumber,
                    continuityPass = passes + 1,
                }),
                CreatedDate = DateTime.UtcNow,
            });
        }

        // Pre-enqueue the next ContinuityCheck so OnEditComplete's "only if no prior continuity"
        // check doesn't accidentally short-circuit it. It'll sit Pending while Edits run.
        db.AIJobQueue.Add(new AIJobQueueEntry
        {
            Id = Guid.NewGuid(),
            BookProjectId = job.BookProjectId,
            BookRequestId = job.BookRequestId,
            JobType = AIJobType.ContinuityCheck,
            Priority = job.Priority,
            Status = AIJobStatus.Pending,
            InputJson = JsonSerializer.Serialize(new { continuityPass = passes + 1 }),
            CreatedDate = DateTime.UtcNow.AddSeconds(1), // sequence after the Edits in claim order
        });

        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Continuity pass {Pass} found issues on {Count} chapters of project {ProjectId}; re-edit + recheck enqueued.",
            passes, chapters.Count, job.BookProjectId);
    }

    private static List<int> TryReadChaptersNeedingRevision(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return new();
        try
        {
            using var doc = JsonDocument.Parse(outputJson);
            if (doc.RootElement.TryGetProperty("chaptersNeedingRevision", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                return arr.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Number)
                    .Select(e => e.GetInt32())
                    .ToList();
            }
        }
        catch { /* ignore */ }
        return new();
    }

    private async Task OnMetadataComplete(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        await EnqueueIfMissing(db, job, AIJobType.GenerateMarketing, ct);
    }

    private async Task OnMarketingComplete(MadAuthorDbContext db, AIJobQueueEntry job, CancellationToken ct)
    {
        var project = await db.BookProjects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == job.BookProjectId, ct);
        if (project is null) return;

        // Idempotency guard: the reconciler re-invokes HandleCompleted on every Completed job on
        // every tick. The column updates below are idempotent, but the InApp notification and the
        // owner email further down are NOT — without this guard a finished book spams the owner
        // every 2 minutes forever. Treat "we've already inserted the JobCompleted notification
        // pointing at this project" as the marker that we've fired the once-only side effects.
        var alreadyNotified = await db.Notifications.AnyAsync(n =>
            n.UserId == project.OwnerUserId
            && n.Type == NotificationType.JobCompleted
            && n.LinkUrl == $"/books/{project.Id}", ct);
        if (alreadyNotified)
        {
            // Still ensure project columns reflect the terminal state in case anyone edited them.
            // No SaveChangesAsync needed if values match — EF tracks changes.
            project.Status = BookProjectStatus.ReadyForReview;
            project.WorkflowStage = BookProjectWorkflowStage.Publishing;
            project.CompletionPercentage = 100;
            await db.SaveChangesAsync(ct);
            return;
        }

        project.Status = BookProjectStatus.ReadyForReview;
        project.WorkflowStage = BookProjectWorkflowStage.Publishing;
        project.CompletionPercentage = 100;
        project.UpdatedDate = DateTime.UtcNow;

        // Auto-queue the standard export bundle (PDF / EPUB / DOCX) once marketing is done.
        // ExportRendererService polls BookExports.Status=Queued and renders. Idempotent against
        // existing rows (skip if any Queued or Running already exists per format).
        var standardFormats = new[] { BookExportType.Pdf, BookExportType.Epub, BookExportType.Docx };
        foreach (var fmt in standardFormats)
        {
            var alreadyInFlight = await db.BookExports.AnyAsync(e =>
                e.BookProjectId == project.Id
                && e.ExportType == fmt
                && (e.Status == BookExportStatus.Queued
                    || e.Status == BookExportStatus.Running
                    || e.Status == BookExportStatus.Ready), ct);
            if (alreadyInFlight) continue;

            db.BookExports.Add(new BookExport
            {
                Id = Guid.NewGuid(),
                BookProjectId = project.Id,
                ExportType = fmt,
                Status = BookExportStatus.Queued,
                CreatedDate = DateTime.UtcNow,
            });
        }

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = project.OwnerUserId,
            CompanyId = project.CompanyId,
            Type = NotificationType.JobCompleted,
            Title = "Your book is ready",
            Message = $"\"{project.Title}\" finished generation. Open the project to read it.",
            LinkUrl = $"/books/{project.Id}",
            Channel = NotificationChannel.InApp,
            DeliveryStatus = DeliveryStatus.Pending,
            CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        // Email the owner that the pipeline is done. Resolves via the orchestrator's scope.
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var owner = await users.FindByIdAsync(project.OwnerUserId.ToString());
        if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
        {
            var subject = $"Your book is ready: {project.Title}";
            var html = $"""
                <p>Hi {System.Net.WebUtility.HtmlEncode(owner.FirstName)},</p>
                <p><strong>{System.Net.WebUtility.HtmlEncode(project.Title)}</strong> finished generation.
                Open it in MADAuthor to read, regenerate chapters, and export.</p>
                <p style="margin-top:16px;">— MADAuthor</p>
                """;
            await email.SendAsync(owner.Email!, $"{owner.FirstName} {owner.LastName}".Trim(), subject, html, ct);
        }

        log.LogInformation("Project {ProjectId} pipeline complete.", project.Id);
    }

    private static async Task EnqueueIfMissing(
        MadAuthorDbContext db, AIJobQueueEntry source, AIJobType jobType, CancellationToken ct)
    {
        var exists = await db.AIJobQueue.AnyAsync(j =>
            j.BookProjectId == source.BookProjectId
            && j.JobType == jobType
            && j.Status != AIJobStatus.Failed
            && j.Status != AIJobStatus.Cancelled, ct);
        if (exists) return;

        db.AIJobQueue.Add(new AIJobQueueEntry
        {
            Id = Guid.NewGuid(),
            BookProjectId = source.BookProjectId,
            BookRequestId = source.BookRequestId,
            JobType = jobType,
            Priority = source.Priority,
            Status = AIJobStatus.Pending,
            CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static Guid? ExtractChapterId(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.TryGetProperty("chapterId", out var el)
                && el.ValueKind == JsonValueKind.String
                && Guid.TryParse(el.GetString(), out var id))
                return id;
        }
        catch { /* ignore */ }
        return null;
    }
}
