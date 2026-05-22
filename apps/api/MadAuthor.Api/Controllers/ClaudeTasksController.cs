using System.Text.Json;
using MadAuthor.Api.Realtime;
using MadAuthor.Application.ClaudeTasks;
using MadAuthor.Application.Storage;
using MadAuthor.Contracts.ClaudeTasks;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace MadAuthor.Api.Controllers;

/// <summary>
/// Operator/dev task queue. See <c>docs/08-claude-task-system.md</c>. Surface area:
/// <list type="bullet">
///   <item><c>GET    /api/claude-tasks</c> — bucketed (active / to-deploy / terminal).</item>
///   <item><c>GET    /api/claude-tasks/next</c> — worker poll. 200 <c>{task}</c> or 204.</item>
///   <item><c>GET    /api/claude-tasks/{id}</c></item>
///   <item><c>POST   /api/claude-tasks</c></item>
///   <item><c>PATCH  /api/claude-tasks/{id}</c> — partial; terminal statuses need <c>?override=true</c>.</item>
///   <item><c>DELETE /api/claude-tasks/{id}</c></item>
///   <item><c>POST   /api/claude-tasks/import-bulk</c> — dedupe by trim+lowercase title vs PENDING+IN_PROGRESS.</item>
///   <item><c>POST   /api/claude-tasks/{id}/attachments</c> (multipart)</item>
///   <item><c>GET    /api/claude-tasks/{id}/attachments/{filename}/download</c></item>
///   <item><c>DELETE /api/claude-tasks/{id}/attachments/{filename}</c></item>
/// </list>
/// Every mutation broadcasts a <see cref="ClaudeTaskEvent"/> to the
/// <c>NotificationHub.ClaudeTasksGroup</c>.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Owner")]
[Route("api/claude-tasks")]
public class ClaudeTasksController(
    MadAuthorDbContext db,
    IFileStorage storage,
    IHubContext<NotificationHub> hub) : ControllerBase
{
    private const string AttachmentsContainer = "claude-task-attachments";
    private const long MaxBytesPerFile = 10L * 1024 * 1024;   // 10 MB per file
    private const long MaxBytesPerRequest = 50L * 1024 * 1024; // 50 MB total per upload request
    private const int MaxAttachmentsPerTask = 10;

    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif",
        "application/pdf",
        "text/plain", "text/csv", "application/json",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Status sets are defined once in ClaudeTaskStateMachine and re-used here. Keeps
    // the controller and the unit tests on the same source of truth.

    // ==========================================================================================
    // QUERIES
    // ==========================================================================================

    /// <summary>Bucketed list. Active and to-deploy are full; terminal is capped to <paramref name="limit"/> (default 25).</summary>
    [HttpGet]
    public async Task<ActionResult<ClaudeTaskListResponse>> List(
        [FromQuery] int limit = 25,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 9999) limit = 25;

        // Fetch all active + to-deploy (small set; queue not log), plus the most-recent N terminal.
        var activeAndDeploy = await db.ClaudeTasks
            .Where(t => t.Status != ClaudeTaskStatus.Completed
                     && t.Status != ClaudeTaskStatus.Cancelled
                     && t.Status != ClaudeTaskStatus.Failed)
            .OrderBy(t => t.Priority).ThenBy(t => t.Id)
            .ToListAsync(ct);

        var terminal = await db.ClaudeTasks
            .Where(t => t.Status == ClaudeTaskStatus.Completed
                     || t.Status == ClaudeTaskStatus.Cancelled
                     || t.Status == ClaudeTaskStatus.Failed)
            .OrderByDescending(t => t.CreatedDate)
            .Take(limit)
            .ToListAsync(ct);

        var active = activeAndDeploy.Where(t => ClaudeTaskStateMachine.Active.Contains(t.Status))
                                     .Select(ToSummary).ToList();
        var toBeDeployed = activeAndDeploy.Where(t => t.Status == ClaudeTaskStatus.ToBeDeployed)
                                          .Select(ToSummary).ToList();
        var terminalDtos = terminal.Select(ToSummary).ToList();

        return new ClaudeTaskListResponse(active, toBeDeployed, terminalDtos);
    }

    /// <summary>Worker poll endpoint. 200 <c>{task}</c> from the active bucket, or 204.</summary>
    /// <remarks>
    /// Picks priority-then-id ascending (FIFO within priority). Workers MUST PATCH to
    /// IN_PROGRESS the instant they pick the task, before doing any code work, so the
    /// operator UI reflects state in real time.
    /// </remarks>
    [HttpGet("next")]
    public async Task<ActionResult<ClaudeTaskNextResponse>> Next(CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks
            .Where(t => ClaudeTaskStateMachine.Active.Contains(t.Status))
            .OrderBy(t => t.Priority).ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (task is null) return NoContent();
        return new ClaudeTaskNextResponse(ToDetail(task));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaudeTaskDetail>> Get(int id, CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks.FindAsync(new object[] { id }, ct);
        if (task is null) return NotFound();
        return ToDetail(task);
    }

    // ==========================================================================================
    // MUTATIONS
    // ==========================================================================================

    [HttpPost]
    public async Task<ActionResult<ClaudeTaskDetail>> Create(
        [FromBody] CreateClaudeTaskRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { error = "Title is required." });
        if (req.Title.Length > 300)
            return BadRequest(new { error = "Title must be 300 characters or fewer." });

        var task = new ClaudeTask
        {
            Title = req.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes,
            Status = req.Status ?? ClaudeTaskStatus.Pending,
            Priority = req.Priority is >= 1 and <= 4 ? req.Priority.Value : (byte)3,
            CreatedDate = DateTime.UtcNow,
        };
        db.ClaudeTasks.Add(task);
        await db.SaveChangesAsync(ct);

        var detail = ToDetail(task);
        await Broadcast("task.created", task.Id, detail, ct);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, detail);
    }

    /// <summary>Partial update. Only non-null fields are written.</summary>
    /// <remarks>
    /// Terminal-status changes require <c>?override=true</c> (operator escape hatch for
    /// notes-only corrections). Otherwise the state-machine validator rejects illegal
    /// transitions with 400.
    /// </remarks>
    [HttpPatch("{id:int}")]
    public async Task<ActionResult<ClaudeTaskDetail>> Update(
        int id,
        [FromBody] UpdateClaudeTaskRequest req,
        [FromQuery(Name = "override")] bool overrideTerminal = false,
        CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks.FindAsync(new object[] { id }, ct);
        if (task is null) return NotFound();

        if (req.Title is not null)
        {
            if (req.Title.Length is 0 or > 300)
                return BadRequest(new { error = "Title must be 1-300 characters." });
            task.Title = req.Title.Trim();
        }
        // Note: empty string is a valid "clear" for description/notes -- the operator UI
        // sends "" to wipe the field. We treat null as "not provided", "" as "clear".
        if (req.Description is not null)
            task.Description = string.IsNullOrEmpty(req.Description) ? null : req.Description;
        if (req.Notes is not null)
            task.Notes = string.IsNullOrEmpty(req.Notes) ? null : req.Notes;
        if (req.Priority is { } p)
        {
            if (p is < 1 or > 4) return BadRequest(new { error = "Priority must be 1-4." });
            task.Priority = p;
        }
        if (req.Status is { } newStatus && newStatus != task.Status)
        {
            var transitionError = ClaudeTaskStateMachine.ValidateTransition(task.Status, newStatus, overrideTerminal);
            if (transitionError is not null) return BadRequest(new { error = transitionError });
            task.Status = newStatus;
        }

        await db.SaveChangesAsync(ct);

        var detail = ToDetail(task);
        await Broadcast("task.updated", task.Id, detail, ct);
        return detail;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks.FindAsync(new object[] { id }, ct);
        if (task is null) return NotFound();

        // Best-effort clean up attachment files. A failed unlink should not block the row
        // delete -- the worst case is an orphaned blob the operator can prune later.
        foreach (var att in ParseAttachments(task))
        {
            try { await storage.DeleteAsync(AttachmentsContainer, att.Filename, ct); }
            catch { /* swallow */ }
        }

        db.ClaudeTasks.Remove(task);
        await db.SaveChangesAsync(ct);

        await Broadcast("task.deleted", id, task: null, ct);
        return NoContent();
    }

    /// <summary>
    /// Bulk PENDING insert. Dedupe by trim+lowercase title against the current active queue
    /// (Pending + InProgress) AND within the incoming payload. Returns the IDs of created
    /// rows and the titles that were skipped.
    /// </summary>
    [HttpPost("import-bulk")]
    public async Task<ActionResult<ImportBulkClaudeTasksResponse>> ImportBulk(
        [FromBody] ImportBulkClaudeTasksRequest req,
        CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Count == 0)
            return BadRequest(new { error = "items[] must be non-empty." });
        if (req.Items.Count > 200)
            return BadRequest(new { error = "import-bulk accepts at most 200 items per call." });

        // Snapshot active titles for dedupe. Pending + InProgress only -- terminal rows
        // (Completed/Failed/Cancelled) do NOT dedupe so a scanner can re-queue a previously
        // failed task once the blocker is resolved.
        var activeTitles = await db.ClaudeTasks
            .Where(t => t.Status == ClaudeTaskStatus.Pending || t.Status == ClaudeTaskStatus.InProgress)
            .Select(t => t.Title)
            .ToListAsync(ct);
        var activeSet = new HashSet<string>(
            activeTitles.Select(ClaudeTaskStateMachine.NormaliseTitle),
            StringComparer.Ordinal);
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        var createdIds = new List<int>();
        var skippedTitles = new List<string>();
        var createdDetails = new List<ClaudeTaskDetail>();

        foreach (var item in req.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 300)
            {
                skippedTitles.Add(item.Title ?? string.Empty);
                continue;
            }
            var key = ClaudeTaskStateMachine.NormaliseTitle(item.Title);
            if (activeSet.Contains(key) || seenInBatch.Contains(key))
            {
                skippedTitles.Add(item.Title);
                continue;
            }
            seenInBatch.Add(key);

            var task = new ClaudeTask
            {
                Title = item.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes,
                Status = ClaudeTaskStatus.Pending,
                Priority = item.Priority is >= 1 and <= 4 ? item.Priority.Value : (byte)3,
                CreatedDate = DateTime.UtcNow,
            };
            db.ClaudeTasks.Add(task);
            createdDetails.Add(default!); // placeholder; populated post-save below
        }

        await db.SaveChangesAsync(ct);

        // Now that IDs are assigned, build detail DTOs + broadcast each create.
        var idx = 0;
        foreach (var entry in db.ChangeTracker.Entries<ClaudeTask>())
        {
            if (entry.State != EntityState.Unchanged || idx >= createdDetails.Count) continue;
            createdIds.Add(entry.Entity.Id);
            createdDetails[idx] = ToDetail(entry.Entity);
            await Broadcast("task.created", entry.Entity.Id, createdDetails[idx], ct);
            idx++;
        }

        // Fallback path: ChangeTracker may not preserve order across SaveChanges in all EF
        // configurations, so if the loop above missed any, re-derive from the DB.
        if (createdIds.Count == 0 && createdDetails.Count > 0)
        {
            var freshlyCreated = await db.ClaudeTasks
                .Where(t => t.CreatedDate >= createdDetails[0]!.CreatedDate.AddSeconds(-1))
                .OrderByDescending(t => t.Id)
                .Take(createdDetails.Count)
                .ToListAsync(ct);
            createdIds.AddRange(freshlyCreated.Select(t => t.Id));
            foreach (var t in freshlyCreated)
                await Broadcast("task.created", t.Id, ToDetail(t), ct);
        }

        return new ImportBulkClaudeTasksResponse(
            Created: createdIds.Count,
            Skipped: skippedTitles.Count,
            CreatedIds: createdIds,
            SkippedTitles: skippedTitles);
    }

    // ==========================================================================================
    // ATTACHMENTS
    // ==========================================================================================

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(MaxBytesPerRequest + 1024)]
    public async Task<ActionResult<ClaudeTaskDetail>> AddAttachments(
        int id,
        [FromForm] List<IFormFile> files,
        CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks.FindAsync(new object[] { id }, ct);
        if (task is null) return NotFound();
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        var existing = ParseAttachments(task).ToList();
        if (existing.Count + files.Count > MaxAttachmentsPerTask)
            return BadRequest(new { error = $"At most {MaxAttachmentsPerTask} attachments per task." });

        var added = new List<ClaudeTaskAttachment>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            if (file.Length > MaxBytesPerFile)
                return BadRequest(new { error = $"File '{file.FileName}' exceeds {MaxBytesPerFile / 1024 / 1024} MB." });

            var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            if (!AllowedMime.Contains(mime))
                return BadRequest(new { error = $"File type not allowed: {mime}" });

            var safeName = Path.GetFileName(file.FileName);
            var keyHint = $"{id}/{Guid.NewGuid():N}-{safeName}";

            string storedKey;
            await using (var stream = file.OpenReadStream())
            {
                storedKey = await storage.SaveAsync(AttachmentsContainer, keyHint, stream, ct);
            }

            // Operator UI fetches via the download endpoint -- the key in storage IS the
            // relative path we serve. Public URL is built off the controller route.
            var url = Url.Action(nameof(DownloadAttachment),
                values: new { id, filename = Uri.EscapeDataString(storedKey) })
                ?? $"/api/claude-tasks/{id}/attachments/{Uri.EscapeDataString(storedKey)}/download";

            added.Add(new ClaudeTaskAttachment(
                Filename: storedKey,
                OriginalName: safeName,
                MimeType: mime,
                Size: file.Length,
                Url: url));
        }

        existing.AddRange(added);
        task.AttachmentsJson = JsonSerializer.Serialize(existing, JsonOpts);
        await db.SaveChangesAsync(ct);

        var detail = ToDetail(task);
        await Broadcast("task.updated", task.Id, detail, ct);
        return detail;
    }

    [HttpGet("{id:int}/attachments/{filename}/download")]
    public async Task<IActionResult> DownloadAttachment(int id, string filename, CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks.FindAsync(new object[] { id }, ct);
        if (task is null) return NotFound();

        var attachments = ParseAttachments(task);
        var decoded = Uri.UnescapeDataString(filename);
        var match = attachments.FirstOrDefault(a => a.Filename == decoded
            || a.Filename.EndsWith("/" + decoded, StringComparison.Ordinal));
        if (match is null) return NotFound();

        var path = storage.ResolvePath(AttachmentsContainer, match.Filename);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers.Append(HeaderNames.ContentDisposition,
            new ContentDispositionHeaderValue("inline") { FileNameStar = match.OriginalName }.ToString());

        var stream = System.IO.File.OpenRead(path);
        return File(stream, match.MimeType, match.OriginalName);
    }

    [HttpDelete("{id:int}/attachments/{filename}")]
    public async Task<IActionResult> RemoveAttachment(int id, string filename, CancellationToken ct = default)
    {
        var task = await db.ClaudeTasks.FindAsync(new object[] { id }, ct);
        if (task is null) return NotFound();

        var attachments = ParseAttachments(task).ToList();
        var decoded = Uri.UnescapeDataString(filename);
        var match = attachments.FirstOrDefault(a => a.Filename == decoded
            || a.Filename.EndsWith("/" + decoded, StringComparison.Ordinal));
        if (match is null) return NotFound();

        try { await storage.DeleteAsync(AttachmentsContainer, match.Filename, ct); }
        catch { /* best-effort */ }

        attachments.Remove(match);
        task.AttachmentsJson = attachments.Count == 0
            ? null
            : JsonSerializer.Serialize(attachments, JsonOpts);
        await db.SaveChangesAsync(ct);

        await Broadcast("task.updated", task.Id, ToDetail(task), ct);
        return NoContent();
    }

    // ==========================================================================================
    // HELPERS
    // ==========================================================================================

    private static ClaudeTaskSummary ToSummary(ClaudeTask t) => new(
        Id: t.Id,
        Title: t.Title,
        Description: t.Description,
        Notes: t.Notes,
        Status: t.Status,
        Priority: t.Priority,
        AttachmentCount: ParseAttachments(t).Count,
        CreatedDate: t.CreatedDate,
        UpdatedDate: t.UpdatedDate);

    private static ClaudeTaskDetail ToDetail(ClaudeTask t) => new(
        Id: t.Id,
        Title: t.Title,
        Description: t.Description,
        Notes: t.Notes,
        Status: t.Status,
        Priority: t.Priority,
        Attachments: ParseAttachments(t),
        CreatedDate: t.CreatedDate,
        UpdatedDate: t.UpdatedDate);

    private static IReadOnlyList<ClaudeTaskAttachment> ParseAttachments(ClaudeTask t)
    {
        if (string.IsNullOrWhiteSpace(t.AttachmentsJson)) return Array.Empty<ClaudeTaskAttachment>();
        try
        {
            return JsonSerializer.Deserialize<List<ClaudeTaskAttachment>>(t.AttachmentsJson, JsonOpts)
                ?? new List<ClaudeTaskAttachment>();
        }
        catch
        {
            // Defensive: a corrupted JSON column should not 500 the whole request.
            return Array.Empty<ClaudeTaskAttachment>();
        }
    }

    private async Task Broadcast(string type, int taskId, ClaudeTaskDetail? task, CancellationToken ct)
    {
        try
        {
            await hub.Clients.Group(NotificationHub.ClaudeTasksGroup)
                .SendAsync("ClaudeTaskEvent", new ClaudeTaskEvent(type, taskId, task), ct);
        }
        catch
        {
            // Hub failures must not break the HTTP response. Operator UI re-reconciles on
            // page-level Refresh anyway.
        }
    }
}
