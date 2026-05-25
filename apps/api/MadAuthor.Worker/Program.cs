using System.Text.Json;
using DotNetEnv;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using MadAuthor.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// madauthor-worker - CLI invoked by the Claude Code Desktop worker session.
// Subcommands are single-purpose, JSON in / JSON out, so the agent doesn't
// need to know any SQL. See docs/03-worker-and-job-lifecycle.md.
// ---------------------------------------------------------------------------

Env.TraversePath().Load();

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connStr = ResolveConnectionString(config);
var dbOptions = new DbContextOptionsBuilder<MadAuthorDbContext>()
    .UseSqlServer(connStr)
    .Options;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var workerId = Environment.GetEnvironmentVariable("WORKER_ID")
    ?? $"{Environment.MachineName}-{Environment.ProcessId}";

try
{
    return args[0] switch
    {
        "claim"              => await ClaimJob(dbOptions, workerId),
        "claim-batch"        => await ClaimBatch(dbOptions, workerId,
                                                 int.Parse(RequireArg(args, 1, "maxN"))),
        "release"            => await ReleaseClaim(dbOptions, RequireArg(args, 1, "jobId")),
        "context"            => await GetContext(dbOptions, RequireArg(args, 1, "jobId")),
        "progress"           => await UpdateProgress(dbOptions, RequireArg(args, 1, "jobId"),
                                                     RequireArg(args, 2, "stage"),
                                                     int.Parse(RequireArg(args, 3, "percent"))),
        "write-planning"     => await WritePlanning(dbOptions, RequireArg(args, 1, "jobId"),
                                                    Console.In.ReadToEnd()),
        "write-chapter"      => await WriteChapter(dbOptions, RequireArg(args, 1, "jobId"),
                                                   Console.In.ReadToEnd(), final: false),
        "write-edited-chapter" => await WriteChapter(dbOptions, RequireArg(args, 1, "jobId"),
                                                     Console.In.ReadToEnd(), final: true),
        "write-research"     => await WriteAsset(dbOptions, RequireArg(args, 1, "jobId"),
                                                 Console.In.ReadToEnd(), "research-dossier.json"),
        "write-continuity"   => await WriteContinuity(dbOptions, RequireArg(args, 1, "jobId"),
                                                      Console.In.ReadToEnd()),
        "write-metadata"     => await WriteMetadata(dbOptions, RequireArg(args, 1, "jobId"),
                                                    Console.In.ReadToEnd()),
        "write-marketing"    => await WriteAsset(dbOptions, RequireArg(args, 1, "jobId"),
                                                 Console.In.ReadToEnd(), "marketing-kit.json"),
        "complete"           => await CompleteJob(dbOptions, RequireArg(args, 1, "jobId"),
                                                  args.Length > 2 ? args[2] : null),
        "fail"               => await FailJob(dbOptions, RequireArg(args, 1, "jobId"),
                                              RequireArg(args, 2, "message"),
                                              args.Length > 3 && args[3] == "--retry"),
        "heartbeat"          => await Heartbeat(dbOptions, workerId,
                                                args.Length > 1 ? Guid.Parse(args[1]) : (Guid?)null),
        "--help" or "-h"     => PrintUsageAndOk(),
        _ => UnknownCommand(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        ok = false,
        error = ex.Message,
        type = ex.GetType().Name,
    }));
    return 2;
}

// ---- subcommands ----------------------------------------------------------

static async Task<int> ClaimJob(DbContextOptions<MadAuthorDbContext> opts, string workerId)
{
    await using var db = new MadAuthorDbContext(opts);
    const string sql = @"
;WITH next_job AS (
    SELECT TOP (1) *
    FROM AIJobQueue WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Status = 0
       OR (Status = 1 AND LockExpiresAt < SYSUTCDATETIME())
    ORDER BY Priority ASC, CreatedDate ASC
)
UPDATE next_job
SET Status = 1,
    ClaimedBy = @workerId,
    ClaimedAt = SYSUTCDATETIME(),
    LockExpiresAt = DATEADD(MINUTE, 15, SYSUTCDATETIME()),
    StartedDate = COALESCE(StartedDate, SYSUTCDATETIME()),
    UpdatedDate = SYSUTCDATETIME()
OUTPUT
    INSERTED.Id AS Id,
    INSERTED.BookProjectId AS BookProjectId,
    INSERTED.BookRequestId AS BookRequestId,
    INSERTED.JobType AS JobType,
    INSERTED.RetryCount AS RetryCount;";

    await using var conn = new SqlConnection(db.Database.GetConnectionString());
    await conn.OpenAsync();
    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@workerId", workerId);
    await using var reader = await cmd.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, claimed = false }));
        return 0;
    }

    var jobId = reader.GetGuid(0);
    var projectId = reader.GetGuid(1);
    var requestId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
    var jobType = (AIJobType)reader.GetByte(3);
    var retry = reader.GetByte(4);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        claimed = true,
        jobId,
        bookProjectId = projectId,
        bookRequestId = requestId,
        jobType = jobType.ToString(),
        retryCount = retry,
        workerId,
    }, JsonOpts()));
    return 0;
}

/// <summary>
/// Release a claimed job back to Pending WITHOUT writing any user-visible error message.
/// Use this for diagnostic / system claims that should be invisible to the user - never use
/// <c>fail</c> with a developer string, because <c>JobProgressBroadcaster</c> pipes
/// <c>ErrorMessage</c> straight to the SPA over SignalR. Does NOT bump RetryCount.
/// </summary>
static async Task<int> ReleaseClaim(DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw)
{
    var jobId = Guid.Parse(jobIdRaw);
    await using var db = new MadAuthorDbContext(opts);
    var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");

    job.Status = AIJobStatus.Pending;
    job.ClaimedBy = null;
    job.ClaimedAt = null;
    job.LockExpiresAt = null;
    job.StartedDate = null;
    job.ErrorMessage = null;
    job.UpdatedDate = DateTime.UtcNow;
    await db.SaveChangesAsync();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        jobId = job.Id,
        status = job.Status.ToString(),
        retryCount = job.RetryCount,
    }, JsonOpts()));
    return 0;
}

/// <summary>
/// Atomically claim up to N jobs with wave-based concurrency:
///   - A book with NO chapter at Drafted-or-beyond (cold start) gets at most 1 job per tick -
///     so chapter 1 finishes alone, locking voice/cadence before any parallel drafting.
///   - A book with >= 1 Drafted chapter (warm / in flow) gets up to 3 jobs per tick - subsequent
///     waves can run chapters in parallel because they each have prior prose to read for voice.
///   - Across all books, total claimed is capped at N. Interleaves rn=1 across books first for
///     fairness, then rn=2 across books, etc.
/// </summary>
static async Task<int> ClaimBatch(DbContextOptions<MadAuthorDbContext> opts, string workerId, int maxN)
{
    if (maxN < 1) maxN = 1;
    if (maxN > 16) maxN = 16; // sanity cap

    await using var db = new MadAuthorDbContext(opts);
    const string sql = @"
;WITH book_progress AS (
    -- BookChapterStatus: 0=Planned, 1=Drafting, 2=Drafted, 3=Editing, 4=Final.
    -- '>=2' means 'at least one chapter has prose written'.
    SELECT BookProjectId, COUNT(*) AS draftedCount
    FROM BookChapters
    WHERE Status >= 2
    GROUP BY BookProjectId
),
per_book_next AS (
    SELECT q.*,
           ROW_NUMBER() OVER (PARTITION BY q.BookProjectId ORDER BY q.Priority ASC, q.CreatedDate ASC) AS rn,
           ISNULL(bp.draftedCount, 0) AS draftedCount
    FROM AIJobQueue q WITH (UPDLOCK, READPAST, ROWLOCK)
    LEFT JOIN book_progress bp ON bp.BookProjectId = q.BookProjectId
    WHERE q.Status = 0
       OR (q.Status = 1 AND q.LockExpiresAt < SYSUTCDATETIME())
),
winners AS (
    SELECT TOP (@n) *
    FROM per_book_next
    WHERE (draftedCount = 0 AND rn = 1)        -- cold start: just the first job per book
       OR (draftedCount > 0 AND rn <= 3)       -- warm: up to 3 in parallel per book
    -- Round-robin across books first (all rn=1, then all rn=2, then rn=3),
    -- then tiebreak by priority then by createdDate so older books drain first.
    ORDER BY rn ASC, Priority ASC, CreatedDate ASC
)
UPDATE winners
SET Status = 1,
    ClaimedBy = @workerId,
    ClaimedAt = SYSUTCDATETIME(),
    LockExpiresAt = DATEADD(MINUTE, 15, SYSUTCDATETIME()),
    StartedDate = COALESCE(StartedDate, SYSUTCDATETIME()),
    UpdatedDate = SYSUTCDATETIME()
OUTPUT
    INSERTED.Id AS Id,
    INSERTED.BookProjectId AS BookProjectId,
    INSERTED.BookRequestId AS BookRequestId,
    INSERTED.JobType AS JobType,
    INSERTED.RetryCount AS RetryCount;";

    await using var conn = new SqlConnection(db.Database.GetConnectionString());
    await conn.OpenAsync();
    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@workerId", workerId);
    cmd.Parameters.AddWithValue("@n", maxN);
    await using var reader = await cmd.ExecuteReaderAsync();

    var claims = new List<object>();
    while (await reader.ReadAsync())
    {
        var jobId = reader.GetGuid(0);
        var projectId = reader.GetGuid(1);
        var requestId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        var jobType = (AIJobType)reader.GetByte(3);
        var retry = reader.GetByte(4);

        claims.Add(new
        {
            jobId,
            bookProjectId = projectId,
            bookRequestId = requestId,
            jobType = jobType.ToString(),
            retryCount = retry,
        });
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        claimedCount = claims.Count,
        workerId,
        claims,
    }, JsonOpts()));
    return 0;
}

static async Task<int> GetContext(DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw)
{
    var jobId = Guid.Parse(jobIdRaw);
    await using var db = new MadAuthorDbContext(opts);

    var job = await db.AIJobQueue.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");

    var project = await db.BookProjects.AsNoTracking()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(p => p.Id == job.BookProjectId)
        ?? throw new InvalidOperationException($"BookProject {job.BookProjectId} not found.");

    BookRequest? request = null;
    if (job.BookRequestId is { } rid)
    {
        request = await db.BookRequests.AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == rid);
    }

    var existingChapters = await db.BookChapters.AsNoTracking()
        .IgnoreQueryFilters()
        .Where(c => c.BookProjectId == project.Id)
        .OrderBy(c => c.ChapterNumber)
        .Select(c => new
        {
            c.Id,
            c.ChapterNumber,
            c.Title,
            c.Summary,
            c.WordCount,
            Status = c.Status.ToString(),
            c.ContentMarkdown,
        })
        .ToListAsync();

    var characters = await db.BookCharacters.AsNoTracking()
        .IgnoreQueryFilters()
        .Where(c => c.BookProjectId == project.Id)
        .Select(c => new { c.Name, c.Description, c.Personality, c.Background, c.Goals, c.Conflicts })
        .ToListAsync();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        job = new
        {
            job.Id,
            JobType = job.JobType.ToString(),
            Status = job.Status.ToString(),
            job.Priority,
            job.RetryCount,
            InputJson = JsonDocOrEmpty(job.InputJson),
        },
        project = new
        {
            project.Id,
            project.Title,
            project.Subtitle,
            project.Description,
            project.Genre,
            FictionOrNonfiction = project.FictionOrNonfiction.ToString(),
            project.TargetAudience,
            project.WritingTone,
            project.Language,
            project.TargetWordCount,
            project.TargetReadingLevel,
        },
        request = request is null ? null : new
        {
            request.Id,
            RequestType = request.RequestType.ToString(),
            request.IdeaPrompt,
            request.ExistingContent,
            request.Notes,
            request.AIInstructions,
            request.DesiredTone,
            request.DesiredLength,
            request.POVStyle,
            request.WritingStyle,
            request.ThemesCsv,
            request.KeywordsCsv,
            Variables = JsonDocOrEmpty(request.Variables),
            Features = JsonDocOrEmpty(request.Features),
        },
        existingChapters,
        characters,
    }, JsonOpts()));
    return 0;
}

static async Task<int> WriteChapter(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string content, bool final)
{
    var jobId = Guid.Parse(jobIdRaw);
    if (string.IsNullOrWhiteSpace(content))
        throw new InvalidOperationException($"{(final ? "write-edited-chapter" : "write-chapter")} expects Markdown on stdin.");

    await using var db = new MadAuthorDbContext(opts);
    var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");

    var chapterId = ExtractChapterId(job.InputJson)
        ?? throw new InvalidOperationException($"Job {jobId}.InputJson is missing 'chapterId'.");

    var chapter = await db.BookChapters
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Id == chapterId)
        ?? throw new InvalidOperationException($"BookChapter {chapterId} not found.");

    chapter.ContentMarkdown = content;
    chapter.WordCount = CountWords(content);
    chapter.Status = final ? BookChapterStatus.Final : BookChapterStatus.Drafted;
    chapter.GeneratedByJobId = jobId;
    chapter.UpdatedDate = DateTime.UtcNow;

    var project = await db.BookProjects
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(p => p.Id == chapter.BookProjectId);
    if (project is not null)
    {
        // Roll up project-level progress as chapters land. Crude but enough for the UI.
        var total = await db.BookChapters.IgnoreQueryFilters()
            .CountAsync(c => c.BookProjectId == project.Id);
        var done = await db.BookChapters.IgnoreQueryFilters()
            .CountAsync(c => c.BookProjectId == project.Id &&
                             (final
                                ? c.Status == BookChapterStatus.Final
                                : c.Status >= BookChapterStatus.Drafted));
        if (total > 0)
        {
            project.CompletionPercentage = Math.Clamp(20 + (int)(60.0 * done / total), 20, 80);
            project.WorkflowStage = final
                ? BookProjectWorkflowStage.Editing
                : BookProjectWorkflowStage.Drafting;
        }
        project.UpdatedDate = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        jobId,
        chapterId = chapter.Id,
        chapterNumber = chapter.ChapterNumber,
        wordCount = chapter.WordCount,
        status = chapter.Status.ToString(),
    }, JsonOpts()));
    return 0;
}

static async Task<int> WriteAsset(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string content, string filename)
{
    var jobId = Guid.Parse(jobIdRaw);
    if (string.IsNullOrWhiteSpace(content))
        throw new InvalidOperationException("Expected payload on stdin.");

    await using var db = new MadAuthorDbContext(opts);
    var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");

    var assetId = Guid.NewGuid();
    db.BookAssets.Add(new BookAsset
    {
        Id = assetId,
        BookProjectId = job.BookProjectId,
        AssetType = BookAssetType.Generated,
        FileName = filename,
        StorageProvider = StorageProvider.Local,
        BlobContainer = "generated",
        BlobKey = $"generated/{job.BookProjectId}/{assetId}-{filename}",
        MimeType = "application/json",
        FileSize = System.Text.Encoding.UTF8.GetByteCount(content),
        ScanStatus = ScanStatus.Skipped,
        CreatedDate = DateTime.UtcNow,
    });

    // Persist the payload alongside the asset row by writing to disk too - Phase-1 storage.
    var storageRoot = Environment.GetEnvironmentVariable("STORAGE_LOCAL_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "storage");
    var fullPath = Path.Combine(storageRoot, "generated", job.BookProjectId.ToString(), $"{assetId}-{filename}");
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    await File.WriteAllTextAsync(fullPath, content);

    await db.SaveChangesAsync();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        jobId,
        assetId,
        filename,
        bytes = System.Text.Encoding.UTF8.GetByteCount(content),
    }, JsonOpts()));
    return 0;
}

static async Task<int> WriteContinuity(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string content)
{
    var rc = await WriteAsset(opts, jobIdRaw, content, "continuity-report.json");

    // Parse the report so we can return the list of chapters that need revision -
    // the API-side orchestrator uses this to enqueue follow-up EditChapter jobs.
    try
    {
        var parsed = JsonSerializer.Deserialize<ContinuityReport>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok = true,
            chaptersNeedingRevision = parsed?.ChaptersNeedingRevision ?? new(),
            issuesFound = parsed?.IssuesFound ?? false,
        }, JsonOpts()));
    }
    catch
    {
        // The asset is saved; signal that we couldn't parse follow-ups.
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok = true,
            chaptersNeedingRevision = Array.Empty<int>(),
            issuesFound = false,
            warning = "Could not parse ContinuityReport for follow-ups.",
        }, JsonOpts()));
    }
    return rc;
}

static async Task<int> WriteMetadata(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string content)
{
    var jobId = Guid.Parse(jobIdRaw);
    await using var db = new MadAuthorDbContext(opts);
    var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");

    PublisherOutput? meta = null;
    try { meta = JsonSerializer.Deserialize<PublisherOutput>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
    catch { /* fall through; we still archive the raw blob */ }

    if (meta is not null)
    {
        var project = await db.BookProjects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == job.BookProjectId);
        if (project is not null)
        {
            if (!string.IsNullOrWhiteSpace(meta.KdpDescription)) project.Description = meta.KdpDescription;
            if (!string.IsNullOrWhiteSpace(meta.CopyrightText)) project.CopyrightText = meta.CopyrightText;
            if (!string.IsNullOrWhiteSpace(meta.RefinedSubtitle) && string.IsNullOrWhiteSpace(project.Subtitle))
                project.Subtitle = meta.RefinedSubtitle;
            project.WorkflowStage = BookProjectWorkflowStage.Publishing;
            project.UpdatedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    return await WriteAsset(opts, jobIdRaw, content, "publisher-metadata.json");
}

static async Task<int> UpdateProgress(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string stage, int percent)
{
    var jobId = Guid.Parse(jobIdRaw);
    var clamped = Math.Clamp(percent, 0, 100);
    await using var db = new MadAuthorDbContext(opts);
    var updated = await db.AIJobQueue
        .Where(j => j.Id == jobId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Stage, stage)
            .SetProperty(j => j.Progress, clamped)
            .SetProperty(j => j.Status, AIJobStatus.InProgress)
            .SetProperty(j => j.UpdatedDate, DateTime.UtcNow));
    if (updated == 0)
        throw new InvalidOperationException($"Job {jobId} not found.");

    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, jobId, stage, progress = clamped }));
    return 0;
}

static async Task<int> WritePlanning(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string stdinPayload)
{
    var jobId = Guid.Parse(jobIdRaw);
    if (string.IsNullOrWhiteSpace(stdinPayload))
        throw new InvalidOperationException("write-planning expects a JSON document on stdin.");

    var plan = JsonSerializer.Deserialize<PlannerOutput>(stdinPayload, JsonOpts())
        ?? throw new InvalidOperationException("Could not parse planner output JSON.");
    if (plan.Chapters is null || plan.Chapters.Count == 0)
        throw new InvalidOperationException("Planner output has no chapters.");

    await using var db = new MadAuthorDbContext(opts);
    var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");
    var project = await db.BookProjects
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(p => p.Id == job.BookProjectId)
        ?? throw new InvalidOperationException($"BookProject {job.BookProjectId} not found.");

    // Insert chapter rows (skip duplicates by ChapterNumber to make this idempotent
    // if the planner re-runs).
    var existingNumbers = await db.BookChapters
        .IgnoreQueryFilters()
        .Where(c => c.BookProjectId == project.Id)
        .Select(c => c.ChapterNumber)
        .ToListAsync();

    var inserted = 0;
    foreach (var c in plan.Chapters)
    {
        if (existingNumbers.Contains(c.Number)) continue;
        db.BookChapters.Add(new BookChapter
        {
            Id = Guid.NewGuid(),
            BookProjectId = project.Id,
            ChapterNumber = c.Number,
            Title = (c.Title ?? $"Chapter {c.Number}").Trim(),
            Summary = c.Summary,
            WordCount = 0,
            Status = BookChapterStatus.Planned,
            GeneratedByJobId = jobId,
            CreatedDate = DateTime.UtcNow,
        });
        inserted++;
    }

    project.EstimatedWordCount = plan.EstimatedWordCount ?? project.EstimatedWordCount;
    project.EstimatedPageCount = plan.EstimatedPageCount ?? project.EstimatedPageCount;
    project.WorkflowStage = BookProjectWorkflowStage.Planning;
    project.CompletionPercentage = Math.Max(project.CompletionPercentage, 10);
    project.UpdatedDate = DateTime.UtcNow;

    if (plan.Characters is { Count: > 0 })
    {
        foreach (var ch in plan.Characters)
        {
            db.BookCharacters.Add(new BookCharacter
            {
                Id = Guid.NewGuid(),
                BookProjectId = project.Id,
                Name = ch.Name,
                Description = ch.Description,
                Personality = ch.Personality,
                Background = ch.Background,
                Goals = ch.Goals,
                Conflicts = ch.Conflicts,
                CreatedDate = DateTime.UtcNow,
            });
        }
    }

    await db.SaveChangesAsync();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        jobId,
        chaptersInserted = inserted,
        totalPlannedChapters = plan.Chapters.Count,
    }, JsonOpts()));
    return 0;
}

static async Task<int> CompleteJob(DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string? outputJson)
{
    var jobId = Guid.Parse(jobIdRaw);
    await using var db = new MadAuthorDbContext(opts);
    var updated = await db.AIJobQueue
        .Where(j => j.Id == jobId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, AIJobStatus.Completed)
            .SetProperty(j => j.Progress, 100)
            .SetProperty(j => j.CompletedDate, DateTime.UtcNow)
            .SetProperty(j => j.LockExpiresAt, (DateTime?)null)
            .SetProperty(j => j.OutputJson, outputJson)
            .SetProperty(j => j.UpdatedDate, DateTime.UtcNow));
    if (updated == 0) throw new InvalidOperationException($"Job {jobId} not found.");
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, jobId, status = "Completed" }));
    return 0;
}

static async Task<int> FailJob(
    DbContextOptions<MadAuthorDbContext> opts, string jobIdRaw, string message, bool retry)
{
    var jobId = Guid.Parse(jobIdRaw);
    await using var db = new MadAuthorDbContext(opts);
    var job = await db.AIJobQueue.FirstOrDefaultAsync(j => j.Id == jobId)
        ?? throw new InvalidOperationException($"Job {jobId} not found.");

    var nextRetry = (byte)(job.RetryCount + 1);
    var canRetry = retry && nextRetry <= job.MaxRetries;

    job.ErrorMessage = message;
    job.UpdatedDate = DateTime.UtcNow;

    if (canRetry)
    {
        job.Status = AIJobStatus.Pending;
        job.RetryCount = nextRetry;
        job.ClaimedBy = null;
        job.ClaimedAt = null;
        job.LockExpiresAt = null;
    }
    else
    {
        job.Status = AIJobStatus.Failed;
        job.CompletedDate = DateTime.UtcNow;
        job.LockExpiresAt = null;
    }

    await db.SaveChangesAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = true,
        jobId,
        status = job.Status.ToString(),
        retryCount = job.RetryCount,
        willRetry = canRetry,
    }));
    return 0;
}

static async Task<int> Heartbeat(DbContextOptions<MadAuthorDbContext> opts, string workerId, Guid? lastJobId)
{
    await using var db = new MadAuthorDbContext(opts);
    var existing = await db.WorkerHeartbeats.FirstOrDefaultAsync(h => h.WorkerId == workerId);
    if (existing is null)
    {
        db.WorkerHeartbeats.Add(new WorkerHeartbeat
        {
            Id = Guid.NewGuid(),
            WorkerId = workerId,
            LastPing = DateTime.UtcNow,
            LastJobId = lastJobId,
        });
    }
    else
    {
        existing.LastPing = DateTime.UtcNow;
        if (lastJobId is not null) existing.LastJobId = lastJobId;
    }
    await db.SaveChangesAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, workerId, lastPing = DateTime.UtcNow }));
    return 0;
}

// ---- helpers --------------------------------------------------------------

static string ResolveConnectionString(IConfiguration cfg)
{
    var raw = cfg.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(raw))
    {
        var host = cfg["DB_HOST"] ?? throw new InvalidOperationException("DB_HOST is not set.");
        var user = cfg["DB_USERNAME"] ?? throw new InvalidOperationException("DB_USERNAME is not set.");
        var pass = cfg["DB_PASSWORD"] ?? throw new InvalidOperationException("DB_PASSWORD is not set.");
        var dbName = cfg["DB_DATABASE"] ?? throw new InvalidOperationException("DB_DATABASE is not set.");
        raw = $"Server={host};Database={dbName};User Id={user};Password={pass};";
    }
    var b = new SqlConnectionStringBuilder(raw)
    {
        TrustServerCertificate = true,
        Encrypt = true,
        MultipleActiveResultSets = true,
    };
    return b.ConnectionString;
}

static string RequireArg(string[] args, int i, string name)
{
    if (args.Length <= i) throw new ArgumentException($"Missing required argument: {name}");
    return args[i];
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    PrintUsage();
    return 1;
}

static int PrintUsageAndOk() { PrintUsage(); return 0; }

static void PrintUsage()
{
    Console.WriteLine(@"madauthor-worker - Claude Code Desktop worker CLI

USAGE
  madauthor-worker <command> [args]

COMMANDS
  claim                                  Claim the next Pending job (atomic).
                                         JSON: { claimed, jobId, bookProjectId, jobType, ... }
  context <jobId>                        Print BookProject + BookRequest + chapters + characters as JSON.
  progress <jobId> <stage> <percent>     Update Stage/Progress (sets Status=InProgress).
  write-planning <jobId>                 stdin = PlannerOutput JSON; inserts BookChapter rows.
  write-chapter <jobId>                  stdin = Markdown; sets chapter Status=Drafted.
  write-edited-chapter <jobId>           stdin = Markdown; sets chapter Status=Final.
  write-research <jobId>                 stdin = research JSON; attaches as BookAsset.
  write-continuity <jobId>               stdin = continuity JSON; archives + returns chaptersNeedingRevision.
  write-metadata <jobId>                 stdin = PublisherOutput JSON; updates BookProject + archives.
  write-marketing <jobId>                stdin = MarketerOutput JSON; attaches as BookAsset.
  complete <jobId> [outputJson]          Mark Completed.
  fail <jobId> <message> [--retry]       Mark Failed; with --retry, requeue if MaxRetries not hit.
  heartbeat [lastJobId]                  Upsert WorkerHeartbeat row.

ENVIRONMENT
  WORKER_ID            Override the worker identifier (default: <MachineName>-<PID>).
  Connection string is read from appsettings.json or composed from DB_* env vars.");
}

static System.Text.Json.Nodes.JsonNode? JsonDocOrEmpty(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return null;
    try { return System.Text.Json.Nodes.JsonNode.Parse(s); } catch { return null; }
}

static Guid? ExtractChapterId(string? inputJson)
{
    if (string.IsNullOrWhiteSpace(inputJson)) return null;
    try
    {
        using var doc = JsonDocument.Parse(inputJson);
        if (doc.RootElement.TryGetProperty("chapterId", out var el)
            && el.ValueKind == JsonValueKind.String
            && Guid.TryParse(el.GetString(), out var id)) return id;
    }
    catch { /* ignore */ }
    return null;
}

static int CountWords(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return 0;
    // Strip code fences and headers, then split on whitespace.
    var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", " ");
    return stripped.Split(new[] { ' ', '\t', '\r', '\n' },
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}


static JsonSerializerOptions JsonOpts()
{
    return new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
}
