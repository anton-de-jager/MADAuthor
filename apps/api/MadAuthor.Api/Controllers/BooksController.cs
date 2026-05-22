using MadAuthor.Application.Audit;
using MadAuthor.Application.Auth;
using MadAuthor.Contracts.Books;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/books")]
public class BooksController(
    MadAuthorDbContext db,
    ICurrentUserService currentUser,
    IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookSummary>>> List()
    {
        var (userId, companyId) = Identify();
        var list = await db.BookProjects
            .Where(p => p.CompanyId == companyId && p.OwnerUserId == userId)
            .OrderByDescending(p => p.CreatedDate)
            .Select(p => new BookSummary(
                p.Id, p.Title, p.Subtitle, p.Genre,
                p.Status, p.WorkflowStage, p.CompletionPercentage, p.CreatedDate))
            .ToListAsync();
        return list;
    }

    [HttpPost]
    public async Task<ActionResult<BookSummary>> Create([FromBody] CreateBookRequest req)
    {
        var (userId, companyId) = Identify();

        var author = await db.Authors
            .Where(a => a.UserId == userId && a.CompanyId == companyId)
            .OrderBy(a => a.CreatedDate)
            .FirstOrDefaultAsync();

        var project = new BookProject
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            OwnerUserId = userId,
            AuthorId = author?.Id,
            Title = req.Title.Trim(),
            Subtitle = req.Subtitle?.Trim(),
            Genre = req.Genre?.Trim(),
            FictionOrNonfiction = req.FictionOrNonfiction,
            TargetAudience = req.TargetAudience?.Trim(),
            WritingTone = req.WritingTone?.Trim(),
            Language = string.IsNullOrWhiteSpace(req.Language) ? "en" : req.Language.Trim(),
            TargetWordCount = req.TargetWordCount,
            TargetReadingLevel = req.TargetReadingLevel?.Trim(),
            Status = BookProjectStatus.Draft,
            WorkflowStage = BookProjectWorkflowStage.Intake,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookProjects.Add(project);
        await db.SaveChangesAsync();

        await audit.LogAsync("BookProject", project.Id.ToString(), "Created",
            new { project.Title, project.Genre, project.AuthorId });

        return CreatedAtAction(nameof(Get), new { id = project.Id },
            new BookSummary(project.Id, project.Title, project.Subtitle, project.Genre,
                project.Status, project.WorkflowStage, project.CompletionPercentage, project.CreatedDate));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookDetail>> Get(Guid id)
    {
        var (userId, companyId) = Identify();
        var p = await db.BookProjects
            .Include(x => x.Chapters)
            .Where(x => x.Id == id && x.CompanyId == companyId && x.OwnerUserId == userId)
            .FirstOrDefaultAsync();
        if (p is null) return NotFound();

        var authorName = p.AuthorId.HasValue
            ? await db.Authors.Where(a => a.Id == p.AuthorId.Value).Select(a => a.PenName).FirstOrDefaultAsync()
            : null;

        return new BookDetail(
            p.Id, p.Title, p.Subtitle, p.Description, p.Genre, p.FictionOrNonfiction,
            p.TargetAudience, p.WritingTone, p.Language, p.Status, p.WorkflowStage,
            p.CompletionPercentage, p.TargetWordCount, p.TargetReadingLevel,
            p.RequireOutlineApproval, p.OutlineApprovedAt, p.CreatedDate,
            p.AuthorId, authorName,
            p.Chapters.OrderBy(c => c.ChapterNumber)
                .Select(c => new BookChapterSummary(c.Id, c.ChapterNumber, c.Title, c.Summary, c.WordCount, c.Status))
                .ToList());
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<BookDetail>> Update(Guid id, [FromBody] UpdateBookRequest req, CancellationToken ct)
    {
        var (userId, companyId) = Identify();
        var project = await db.BookProjects
            .Include(x => x.Chapters)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId && p.OwnerUserId == userId, ct);
        if (project is null) return NotFound();

        if (req.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300)
                return BadRequest(new { error = "Title must be 1-300 characters." });
            project.Title = req.Title.Trim();
        }
        if (req.Subtitle is not null)
            project.Subtitle = string.IsNullOrEmpty(req.Subtitle) ? null : req.Subtitle.Trim();
        if (req.Genre is not null)
            project.Genre = string.IsNullOrEmpty(req.Genre) ? null : req.Genre.Trim();
        if (req.FictionOrNonfiction is { } fon)
            project.FictionOrNonfiction = fon;
        if (req.TargetAudience is not null)
            project.TargetAudience = string.IsNullOrEmpty(req.TargetAudience) ? null : req.TargetAudience.Trim();
        if (req.WritingTone is not null)
            project.WritingTone = string.IsNullOrEmpty(req.WritingTone) ? null : req.WritingTone.Trim();
        if (req.Language is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Language))
                return BadRequest(new { error = "Language cannot be empty." });
            project.Language = req.Language.Trim();
        }
        if (req.TargetWordCount is { } twc)
            project.TargetWordCount = twc <= 0 ? null : twc;
        if (req.TargetReadingLevel is not null)
            project.TargetReadingLevel = string.IsNullOrEmpty(req.TargetReadingLevel) ? null : req.TargetReadingLevel.Trim();
        if (req.AuthorId is { } authorId)
        {
            // Validate the author belongs to the same company
            var authorExists = await db.Authors.AnyAsync(a => a.Id == authorId && a.CompanyId == companyId, ct);
            if (!authorExists)
                return BadRequest(new { error = "Author not found in your organisation." });
            project.AuthorId = authorId;
        }

        project.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("BookProject", project.Id.ToString(), "Updated", req);

        var authorName = project.AuthorId.HasValue
            ? await db.Authors.Where(a => a.Id == project.AuthorId.Value).Select(a => a.PenName).FirstOrDefaultAsync(ct)
            : null;

        return new BookDetail(
            project.Id, project.Title, project.Subtitle, project.Description, project.Genre, project.FictionOrNonfiction,
            project.TargetAudience, project.WritingTone, project.Language, project.Status, project.WorkflowStage,
            project.CompletionPercentage, project.TargetWordCount, project.TargetReadingLevel,
            project.RequireOutlineApproval, project.OutlineApprovedAt, project.CreatedDate,
            project.AuthorId, authorName,
            project.Chapters.OrderBy(c => c.ChapterNumber)
                .Select(c => new BookChapterSummary(c.Id, c.ChapterNumber, c.Title, c.Summary, c.WordCount, c.Status))
                .ToList());
    }

    [HttpGet("authors")]
    public async Task<ActionResult<IReadOnlyList<AuthorSummary>>> ListAuthors()
    {
        var (_, companyId) = Identify();
        var authors = await db.Authors
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.PenName)
            .Select(a => new AuthorSummary(a.Id, a.PenName))
            .ToListAsync();
        return authors;
    }

    [HttpPost("{id:guid}/requests")]
    public async Task<ActionResult<object>> Submit(Guid id, [FromBody] SubmitBookRequest req)
    {
        var (userId, companyId) = Identify();
        var project = await db.BookProjects
            .Where(p => p.Id == id && p.CompanyId == companyId && p.OwnerUserId == userId)
            .FirstOrDefaultAsync();
        if (project is null) return NotFound();

        // Stitch all of the project's uploaded asset text into ExistingContent at submit time.
        // This is the canonical place to merge (an asset upload that happened before the request
        // existed would otherwise be invisible to the worker).
        var assetTexts = await db.BookAssets
            .Where(a => a.BookProjectId == project.Id && a.ExtractedText != null)
            .OrderBy(a => a.CreatedDate)
            .Select(a => new { a.FileName, a.ExtractedText })
            .ToListAsync();

        var existingContent = req.ExistingContent ?? string.Empty;
        foreach (var a in assetTexts)
        {
            existingContent += $"\n\n--- Extracted from upload: {a.FileName} ---\n{a.ExtractedText}";
        }
        existingContent = existingContent.Length == 0 ? null! : existingContent;

        var bookRequest = new BookRequest
        {
            Id = Guid.NewGuid(),
            BookProjectId = project.Id,
            RequestType = req.RequestType,
            IdeaPrompt = req.IdeaPrompt,
            ExistingContent = existingContent,
            Notes = req.Notes,
            AIInstructions = req.AIInstructions,
            DesiredTone = req.DesiredTone,
            DesiredLength = req.DesiredLength,
            POVStyle = req.POVStyle,
            WritingStyle = req.WritingStyle,
            ThemesCsv = req.ThemesCsv,
            KeywordsCsv = req.KeywordsCsv,
            Variables = req.Variables is null
                ? "{}"
                : System.Text.Json.JsonSerializer.Serialize(req.Variables),
            Features = req.Features is null
                ? "{}"
                : System.Text.Json.JsonSerializer.Serialize(req.Features),
            TargetPlatformsCsv = req.TargetPlatformsCsv,
            RequestedFormatsCsv = req.RequestedFormatsCsv,
            Priority = req.Priority == 0 ? (byte)5 : req.Priority,
            Status = BookRequestStatus.Queued,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookRequests.Add(bookRequest);

        // Kick off the pipeline by enqueueing a Pending PlanBook job. The Claude Code Desktop
        // worker will pick it up next time it polls. See docs/03-worker-and-job-lifecycle.md.
        var job = new AIJobQueueEntry
        {
            Id = Guid.NewGuid(),
            BookProjectId = project.Id,
            BookRequestId = bookRequest.Id,
            JobType = AIJobType.PlanBook,
            Priority = bookRequest.Priority,
            Status = AIJobStatus.Pending,
            CreatedDate = DateTime.UtcNow,
        };
        db.AIJobQueue.Add(job);

        project.Status = BookProjectStatus.InProgress;
        project.WorkflowStage = BookProjectWorkflowStage.Planning;
        project.UpdatedDate = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(new { bookRequestId = bookRequest.Id, jobId = job.Id });
    }

    [HttpGet("{id:guid}/chapters/{chapterId:guid}")]
    public async Task<ActionResult<object>> GetChapter(Guid id, Guid chapterId)
    {
        var (uid, cid) = Identify();
        var chapter = await db.BookChapters
            .Where(c => c.Id == chapterId && c.BookProjectId == id)
            .Join(db.BookProjects,
                  c => c.BookProjectId,
                  p => p.Id,
                  (c, p) => new { chapter = c, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .Select(x => new
            {
                x.chapter.Id,
                x.chapter.ChapterNumber,
                x.chapter.Title,
                x.chapter.Summary,
                x.chapter.ContentMarkdown,
                x.chapter.WordCount,
                Status = x.chapter.Status.ToString(),
                x.chapter.UpdatedDate,
            })
            .FirstOrDefaultAsync();
        if (chapter is null) return NotFound();
        return Ok(chapter);
    }

    [HttpPost("{id:guid}/approve-outline")]
    public async Task<IActionResult> ApproveOutline(Guid id, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();
        if (project.OutlineApprovedAt is not null) return Ok(new { alreadyApproved = true });

        var plannedChapters = await db.BookChapters
            .Where(c => c.BookProjectId == id && c.Status == Domain.Enums.BookChapterStatus.Planned)
            .ToListAsync(ct);
        if (plannedChapters.Count == 0)
            return BadRequest(new { error = "No planned chapters to approve. Wait for the Planner to finish first." });

        project.OutlineApprovedAt = DateTime.UtcNow;
        project.WorkflowStage = Domain.Enums.BookProjectWorkflowStage.Drafting;
        project.UpdatedDate = DateTime.UtcNow;

        // Directly enqueue DraftChapter jobs — the orchestrator only chains on
        // job completion, and there's no new completion to trigger here.
        foreach (var ch in plannedChapters)
        {
            db.AIJobQueue.Add(new Domain.Entities.AIJobQueueEntry
            {
                Id = Guid.NewGuid(),
                BookProjectId = project.Id,
                JobType = Domain.Enums.AIJobType.DraftChapter,
                Priority = 5,
                Status = Domain.Enums.AIJobStatus.Pending,
                InputJson = System.Text.Json.JsonSerializer.Serialize(
                    new { chapterId = ch.Id, chapterNumber = ch.ChapterNumber }),
                CreatedDate = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { approved = true, draftJobsEnqueued = plannedChapters.Count });
    }

    [HttpPut("{id:guid}/outline")]
    public async Task<ActionResult<IReadOnlyList<BookChapterSummary>>> UpdateOutline(
        Guid id,
        [FromBody] OutlineUpdateRequest request,
        CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (!project.RequireOutlineApproval)
            return Conflict(new { error = "This project does not require outline approval." });
        if (project.OutlineApprovedAt is not null)
            return Conflict(new { error = "The outline has already been approved and can no longer be edited." });

        if (request?.Chapters is null)
            return BadRequest(new { error = "Request body must include a chapters array." });

        // Load all chapters for the project so we can validate state and apply
        // the diff in a single round-trip.
        var existing = await db.BookChapters
            .Where(c => c.BookProjectId == id)
            .ToListAsync(ct);

        // Reject if any chapter has moved beyond Planned — drafting has started
        // and editing the outline at this point is unsafe.
        if (existing.Any(c => c.Status > BookChapterStatus.Planned))
        {
            return Conflict(new
            {
                error = "Drafting has already started on one or more chapters. The outline can no longer be edited.",
            });
        }

        // Validate the incoming payload before mutating anything.
        var incoming = request.Chapters.ToList();
        if (incoming.Any(c => string.IsNullOrWhiteSpace(c.Title)))
            return BadRequest(new { error = "Every chapter must have a non-empty title." });

        var incomingIds = incoming
            .Where(c => c.Id.HasValue)
            .Select(c => c.Id!.Value)
            .ToHashSet();

        // Sanity check: every incoming Id must reference a real Planned chapter
        // on this project. This prevents callers from supplying foreign Guids.
        var existingById = existing.ToDictionary(c => c.Id);
        foreach (var inc in incoming)
        {
            if (inc.Id.HasValue && !existingById.ContainsKey(inc.Id.Value))
                return BadRequest(new { error = $"Unknown chapter id {inc.Id.Value}." });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;

            // Delete any existing chapters that are NOT in the incoming list.
            foreach (var ch in existing)
            {
                if (!incomingIds.Contains(ch.Id))
                    db.BookChapters.Remove(ch);
            }

            // Renormalize ChapterNumber to 1..N based on the ORDER of the
            // incoming list. Trust the client's ordering, not the supplied
            // ChapterNumber values — the UI may have moved rows around without
            // recomputing numbers.
            for (var i = 0; i < incoming.Count; i++)
            {
                var inc = incoming[i];
                var number = i + 1;
                var title = inc.Title.Trim();
                var summary = string.IsNullOrWhiteSpace(inc.Summary) ? null : inc.Summary.Trim();

                if (inc.Id.HasValue && existingById.TryGetValue(inc.Id.Value, out var existingCh))
                {
                    existingCh.ChapterNumber = number;
                    existingCh.Title = title;
                    existingCh.Summary = summary;
                    existingCh.UpdatedDate = now;
                }
                else
                {
                    db.BookChapters.Add(new BookChapter
                    {
                        Id = Guid.NewGuid(),
                        BookProjectId = project.Id,
                        ChapterNumber = number,
                        Title = title,
                        Summary = summary,
                        Status = BookChapterStatus.Planned,
                        CreatedDate = now,
                    });
                }
            }

            project.UpdatedDate = now;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        var refreshed = await db.BookChapters
            .Where(c => c.BookProjectId == id)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new BookChapterSummary(
                c.Id, c.ChapterNumber, c.Title, c.Summary, c.WordCount, c.Status))
            .ToListAsync(ct);
        return Ok(refreshed);
    }

    [HttpPost("{id:guid}/chapters/{chapterId:guid}/regenerate")]
    public async Task<IActionResult> RegenerateChapter(Guid id, Guid chapterId, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var chapter = await db.BookChapters
            .Where(c => c.Id == chapterId && c.BookProjectId == id)
            .Join(db.BookProjects,
                  c => c.BookProjectId,
                  p => p.Id,
                  (c, p) => new { chapter = c, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .Select(x => x.chapter)
            .FirstOrDefaultAsync(ct);
        if (chapter is null) return NotFound();

        chapter.Status = Domain.Enums.BookChapterStatus.Planned;
        chapter.UpdatedDate = DateTime.UtcNow;

        db.AIJobQueue.Add(new Domain.Entities.AIJobQueueEntry
        {
            Id = Guid.NewGuid(),
            BookProjectId = chapter.BookProjectId,
            JobType = Domain.Enums.AIJobType.DraftChapter,
            Priority = 3, // bump priority — user explicitly asked
            Status = Domain.Enums.AIJobStatus.Pending,
            InputJson = System.Text.Json.JsonSerializer.Serialize(
                new { chapterId = chapter.Id, chapterNumber = chapter.ChapterNumber, regenerate = true }),
            CreatedDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { requeued = true });
    }

    private (Guid userId, Guid companyId) Identify()
    {
        if (currentUser.UserId is not { } uid)
            throw new UnauthorizedAccessException("No user id on the request principal.");

        // If the JWT lacks the 'cid' claim (e.g. token issued by an earlier code version
        // before claim was added), fall back to a synchronous DB lookup of the user's
        // first company membership. Mirrors AuthController.Me's defensive pattern.
        if (currentUser.CompanyId is { } cid) return (uid, cid);

        var fallback = db.CompanyMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.CreatedDate)
            .Select(m => (Guid?)m.CompanyId)
            .FirstOrDefault();
        if (fallback is null)
            throw new UnauthorizedAccessException($"User {uid} has no company membership.");
        return (uid, fallback.Value);
    }
}
