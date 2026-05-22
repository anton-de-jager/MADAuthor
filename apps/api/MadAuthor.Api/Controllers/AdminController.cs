using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Owner")]
[Route("api/admin")]
public class AdminController(MadAuthorDbContext db) : ControllerBase
{
    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<object>>> Jobs(
        [FromQuery] string? status = null, [FromQuery] int limit = 50)
    {
        var query = db.AIJobQueue.AsNoTracking();
        if (Enum.TryParse<Domain.Enums.AIJobStatus>(status, ignoreCase: true, out var s))
            query = query.Where(j => j.Status == s);

        var rows = await query
            .OrderByDescending(j => j.CreatedDate)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(j => new
            {
                j.Id,
                j.BookProjectId,
                JobType = j.JobType.ToString(),
                Status = j.Status.ToString(),
                j.Priority,
                j.Stage,
                j.Progress,
                j.RetryCount,
                j.MaxRetries,
                j.ClaimedBy,
                j.ClaimedAt,
                j.StartedDate,
                j.CompletedDate,
                j.ErrorMessage,
                j.CreatedDate,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("heartbeats")]
    public async Task<ActionResult<IReadOnlyList<object>>> Heartbeats()
    {
        var rows = await db.WorkerHeartbeats.AsNoTracking()
            .OrderByDescending(h => h.LastPing)
            .Select(h => new
            {
                h.WorkerId,
                h.LastPing,
                h.LastJobId,
                ageSeconds = (int)(DateTime.UtcNow - h.LastPing).TotalSeconds,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<object>>> Projects()
    {
        var rows = await db.BookProjects.AsNoTracking().IgnoreQueryFilters()
            .OrderByDescending(p => p.CreatedDate)
            .Take(100)
            .Select(p => new
            {
                p.Id,
                p.CompanyId,
                p.OwnerUserId,
                p.Title,
                p.Subtitle,
                Status = p.Status.ToString(),
                WorkflowStage = p.WorkflowStage.ToString(),
                p.CompletionPercentage,
                p.IsDeleted,
                p.CreatedDate,
                p.UpdatedDate,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost("jobs/{jobId:guid}/retry")]
    public async Task<IActionResult> RetryJob(Guid jobId, CancellationToken ct)
    {
        var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();
        if (job.Status != Domain.Enums.AIJobStatus.Failed)
            return BadRequest(new { error = "Only Failed jobs can be retried." });

        job.Status = Domain.Enums.AIJobStatus.Pending;
        job.ClaimedBy = null;
        job.ClaimedAt = null;
        job.LockExpiresAt = null;
        job.ErrorMessage = null;
        job.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { requeued = true });
    }
}
