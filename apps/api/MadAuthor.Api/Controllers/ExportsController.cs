using MadAuthor.Application.Auth;
using MadAuthor.Application.Storage;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace MadAuthor.Api.Controllers;

public record QueueExportsRequest(IReadOnlyList<string> Formats);

[ApiController]
[Authorize]
[Route("api")]
public class ExportsController(
    MadAuthorDbContext db,
    IFileStorage storage,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("books/{projectId:guid}/exports")]
    public async Task<ActionResult<IReadOnlyList<object>>> List(Guid projectId)
    {
        var (uid, cid) = Identify();
        var owns = await db.BookProjects
            .AnyAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid);
        if (!owns) return NotFound();

        var exports = await db.BookExports
            .Where(e => e.BookProjectId == projectId)
            .OrderByDescending(e => e.CreatedDate)
            .Select(e => new
            {
                e.Id,
                ExportType = e.ExportType.ToString(),
                Status = e.Status.ToString(),
                e.FileSize,
                e.ErrorMessage,
                e.ExpiresAt,
                e.DownloadCount,
                e.CreatedDate,
            })
            .ToListAsync();
        return Ok(exports);
    }

    [HttpPost("books/{projectId:guid}/exports")]
    public async Task<ActionResult<object>> Queue(
        Guid projectId, [FromBody] QueueExportsRequest req, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (req.Formats is null || req.Formats.Count == 0)
            return BadRequest(new { error = "No formats requested." });

        var queued = new List<Guid>();
        foreach (var raw in req.Formats.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<BookExportType>(raw, ignoreCase: true, out var et))
                return BadRequest(new { error = $"Unknown export format '{raw}'." });

            // Skip duplicates that are still queued/running so the user can't accidentally hammer the queue.
            var dup = await db.BookExports.AnyAsync(e => e.BookProjectId == projectId
                                                         && e.ExportType == et
                                                         && (e.Status == BookExportStatus.Queued
                                                             || e.Status == BookExportStatus.Running), ct);
            if (dup) continue;

            var export = new BookExport
            {
                Id = Guid.NewGuid(),
                BookProjectId = projectId,
                ExportType = et,
                Status = BookExportStatus.Queued,
                CreatedDate = DateTime.UtcNow,
            };
            db.BookExports.Add(export);
            queued.Add(export.Id);
        }
        await db.SaveChangesAsync(ct);
        return Accepted(new { queued });
    }

    [HttpGet("exports/{exportId:guid}/download")]
    public async Task<IActionResult> Download(Guid exportId, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var record = await db.BookExports
            .Join(db.BookProjects,
                e => e.BookProjectId, p => p.Id,
                (e, p) => new { export = e, project = p })
            .Where(x => x.export.Id == exportId
                        && x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .FirstOrDefaultAsync(ct);
        if (record is null) return NotFound();
        if (record.export.Status != BookExportStatus.Ready || record.export.BlobKey is null)
            return Conflict(new { error = "Export not ready yet." });

        var path = storage.ResolvePath("exports", record.export.BlobKey);
        if (!System.IO.File.Exists(path)) return NotFound();

        record.export.DownloadCount++;
        record.export.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var contentType = record.export.ExportType switch
        {
            BookExportType.Pdf => "application/pdf",
            BookExportType.PrintPdfKdp => "application/pdf",
            BookExportType.PrintPdfIngram => "application/pdf",
            BookExportType.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            BookExportType.Epub => "application/epub+zip",
            BookExportType.Html => "text/html; charset=utf-8",
            BookExportType.Markdown => "text/markdown; charset=utf-8",
            _ => "application/octet-stream",
        };
        var fileName = Path.GetFileName(record.export.BlobKey);
        Response.Headers.Append(HeaderNames.ContentDisposition,
            new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName }.ToString());
        return File(System.IO.File.OpenRead(path), contentType, fileName);
    }

    private (Guid userId, Guid companyId) Identify()
    {
        if (currentUser.UserId is not { } uid || currentUser.CompanyId is not { } cid)
            throw new UnauthorizedAccessException();
        return (uid, cid);
    }
}
