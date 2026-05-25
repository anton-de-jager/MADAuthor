using MadAuthor.Application.Auth;
using MadAuthor.Application.Ingestion;
using MadAuthor.Application.Security;
using MadAuthor.Application.Storage;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/books/{projectId:guid}/assets")]
public class BookAssetsController(
    MadAuthorDbContext db,
    IFileStorage storage,
    ITextExtractor textExtractor,
    IFileScanner fileScanner,
    ILogger<BookAssetsController> log,
    ICurrentUserService currentUser) : ControllerBase
{
    private const long MaxBytes = 50 * 1024 * 1024; // 50 MB per upload
    private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain", "text/markdown",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "image/png", "image/jpeg", "image/webp",
        "audio/mpeg", "audio/wav", "audio/mp4", "audio/x-m4a",
    };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(Guid projectId)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .Where(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();
        if (project == Guid.Empty) return NotFound();

        var assets = await db.BookAssets
            .Where(a => a.BookProjectId == projectId)
            .OrderByDescending(a => a.CreatedDate)
            .Select(a => new
            {
                a.Id,
                AssetType = a.AssetType.ToString(),
                a.FileName,
                a.MimeType,
                a.FileSize,
                ScanStatus = a.ScanStatus.ToString(),
                a.CreatedDate,
            })
            .ToListAsync();
        return Ok(assets);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxBytes + 1024)]
    public async Task<ActionResult<object>> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .Where(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid)
            .FirstOrDefaultAsync(ct);
        if (project is null) return NotFound();

        if (file is null || file.Length == 0) return BadRequest(new { error = "No file." });
        if (file.Length > MaxBytes) return BadRequest(new { error = $"File too large (max {MaxBytes / 1024 / 1024} MB)." });
        var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (!AllowedMime.Contains(mime))
            return BadRequest(new { error = $"Unsupported MIME type: {mime}" });

        var assetId = Guid.NewGuid();
        var safeName = Path.GetFileName(file.FileName);
        var key = $"{cid}/{projectId}/{assetId}-{safeName}";

        // Scan the upload before persisting it. If clamd flags an infection, refuse the upload.
        // If the scanner is disabled or errors, fall through to Skipped - virus scanning is a
        // hardening layer, not a hard dependency for legitimate uploads.
        ScanStatus scanStatus;
        string? scanThreat = null;
        await using (var scanStream = file.OpenReadStream())
        {
            try
            {
                var scan = await fileScanner.ScanAsync(scanStream, safeName, ct);
                scanStatus = scan.Status;
                scanThreat = scan.Threat;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "File scan failed for upload '{File}'; recording Skipped.", safeName);
                scanStatus = ScanStatus.Skipped;
            }
        }

        if (scanStatus == ScanStatus.Infected)
        {
            log.LogWarning("Refusing infected upload '{File}' (threat={Threat}) from user {User}.",
                safeName, scanThreat, uid);
            return UnprocessableEntity(new { error = "File rejected by virus scan.", threat = scanThreat });
        }

        await using (var stream = file.OpenReadStream())
        {
            await storage.SaveAsync("uploads", key, stream, ct);
        }

        var asset = new BookAsset
        {
            Id = assetId,
            BookProjectId = projectId,
            AssetType = BookAssetType.Upload,
            FileName = safeName,
            StorageProvider = StorageProvider.Local,
            BlobContainer = "uploads",
            BlobKey = key,
            MimeType = mime,
            FileSize = file.Length,
            ScanStatus = scanStatus,
            CreatedDate = DateTime.UtcNow,
        };

        // Extract text best-effort, store it on the asset itself. The Submit endpoint stitches
        // all of the project's asset ExtractedText into BookRequest.ExistingContent when a
        // BookRequest is created - so uploads done before Submit are no longer dropped on the floor.
        asset.ExtractedText = await TryExtractText(asset, ct);

        db.BookAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            asset.Id,
            asset.FileName,
            asset.MimeType,
            asset.FileSize,
            createdDate = asset.CreatedDate,
            textExtracted = !string.IsNullOrWhiteSpace(asset.ExtractedText),
            extractedChars = asset.ExtractedText?.Length ?? 0,
        });
    }

    private async Task<string?> TryExtractText(BookAsset asset, CancellationToken ct)
    {
        try
        {
            using var stream = storage.OpenRead(asset.BlobContainer, asset.BlobKey);
            var extracted = await textExtractor.ExtractAsync(asset.MimeType, asset.FileName, stream, ct);
            return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
        }
        catch
        {
            // Best-effort. The asset is still saved; the agent just won't have its text.
            return null;
        }
    }

    [HttpGet("{assetId:guid}/download")]
    public async Task<IActionResult> Download(Guid projectId, Guid assetId)
    {
        var (uid, cid) = Identify();
        var asset = await db.BookAssets
            .Where(a => a.Id == assetId && a.BookProjectId == projectId)
            .Join(db.BookProjects,
                  a => a.BookProjectId,
                  p => p.Id,
                  (a, p) => new { asset = a, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .Select(x => x.asset)
            .FirstOrDefaultAsync();
        if (asset is null) return NotFound();

        var path = storage.ResolvePath(asset.BlobContainer, asset.BlobKey);
        if (!System.IO.File.Exists(path)) return NotFound();

        var cd = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = asset.FileName,
        };
        Response.Headers.Append(HeaderNames.ContentDisposition, cd.ToString());

        var stream = System.IO.File.OpenRead(path);
        return File(stream, asset.MimeType, asset.FileName);
    }

    [HttpDelete("{assetId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid assetId, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var asset = await db.BookAssets
            .Where(a => a.Id == assetId && a.BookProjectId == projectId)
            .Join(db.BookProjects,
                  a => a.BookProjectId,
                  p => p.Id,
                  (a, p) => new { asset = a, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .Select(x => x.asset)
            .FirstOrDefaultAsync(ct);
        if (asset is null) return NotFound();

        await storage.DeleteAsync(asset.BlobContainer, asset.BlobKey, ct);
        db.BookAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private (Guid userId, Guid companyId) Identify()
    {
        if (currentUser.UserId is not { } uid || currentUser.CompanyId is not { } cid)
            throw new UnauthorizedAccessException();
        return (uid, cid);
    }
}
