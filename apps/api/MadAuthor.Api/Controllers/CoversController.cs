using System.Text.Json;
using MadAuthor.Application.Auth;
using MadAuthor.Application.Covers;
using MadAuthor.Application.Storage;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Api.Controllers;

public record CoverSelectRequest(string PhotoId, string? CustomQuery);

public record GenerateAiCoverRequest(string? Prompt, string? Style);

[ApiController]
[Authorize]
[Route("api/books/{projectId:guid}/covers")]
public class CoversController(
    MadAuthorDbContext db,
    IUnsplashClient unsplash,
    IImageGenerator imageGen,
    IFileStorage storage,
    ICurrentUserService currentUser,
    ILogger<CoversController> log) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(Guid projectId)
    {
        var (uid, cid) = Identify();
        var owns = await db.BookProjects
            .AnyAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid);
        if (!owns) return NotFound();

        var covers = await db.BookCovers
            .Where(c => c.BookProjectId == projectId)
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new
            {
                c.Id,
                c.Prompt,
                c.Style,
                c.AssetId,
                Status = c.Status.ToString(),
                c.CreatedDate,
                AssetUrl = c.AssetId == null
                    ? null
                    : $"/api/books/{projectId}/assets/{c.AssetId}/download",
                Attribution = c.AssetId == null
                    ? null
                    : db.BookAssets.Where(a => a.Id == c.AssetId)
                        .Select(a => a.AttributionJson)
                        .FirstOrDefault(),
            })
            .ToListAsync();
        return Ok(covers);
    }

    [HttpGet("search")]
    public async Task<ActionResult<object>> Search(Guid projectId, [FromQuery] string q, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var owns = await db.BookProjects
            .AnyAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (!owns) return NotFound();

        if (!unsplash.IsConfigured)
            return StatusCode(503, new { error = "UNSPLASH_ACCESS_KEY is not configured on the server." });
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query is required." });

        try
        {
            var photos = await unsplash.SearchAsync(q, perPage: 12, ct);
            return Ok(photos.Select(p => new
            {
                p.Id,
                p.AltDescription,
                p.Description,
                thumbUrl = p.Urls.Small,
                previewUrl = p.Urls.Regular,
                p.Width,
                p.Height,
                p.Color,
                photographer = new
                {
                    name = p.User.Name,
                    profileUrl = p.User.Links.Html,
                    username = p.User.Username,
                },
                photoUrl = p.Links.Html,
            }));
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = "Unsplash search failed.", detail = ex.Message });
        }
    }

    [HttpPost("select")]
    public async Task<ActionResult<object>> Select(
        Guid projectId, [FromBody] CoverSelectRequest req, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (!unsplash.IsConfigured)
            return StatusCode(503, new { error = "UNSPLASH_ACCESS_KEY is not configured on the server." });
        if (string.IsNullOrWhiteSpace(req.PhotoId))
            return BadRequest(new { error = "PhotoId is required." });

        var photo = await unsplash.GetAsync(req.PhotoId, ct);
        if (photo is null)
            return NotFound(new { error = $"Unsplash photo '{req.PhotoId}' not found." });

        // Honor Unsplash API guideline: hit the tracking URL when fetching the image.
        var (bytes, contentType) = await unsplash.DownloadAsync(photo.Links.DownloadLocation, ct);

        var assetId = Guid.NewGuid();
        var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        var fileName = $"cover-unsplash-{photo.Id}.{ext}";
        var key = $"{cid}/{projectId}/{assetId}-{fileName}";

        using (var ms = new MemoryStream(bytes))
        {
            await storage.SaveAsync("covers", key, ms, ct);
        }

        var attribution = JsonSerializer.Serialize(new
        {
            source = "Unsplash",
            photoId = photo.Id,
            name = photo.User.Name,
            username = photo.User.Username,
            url = photo.User.Links.Html,
            photoUrl = photo.Links.Html,
            altDescription = photo.AltDescription,
            width = photo.Width,
            height = photo.Height,
        });

        var asset = new BookAsset
        {
            Id = assetId,
            BookProjectId = projectId,
            AssetType = BookAssetType.Cover,
            FileName = fileName,
            StorageProvider = StorageProvider.Local,
            BlobContainer = "covers",
            BlobKey = key,
            MimeType = contentType,
            FileSize = bytes.LongLength,
            ScanStatus = ScanStatus.Skipped,
            AttributionJson = attribution,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookAssets.Add(asset);

        // Demote any previously selected covers for this project.
        var prior = await db.BookCovers
            .Where(c => c.BookProjectId == projectId && c.Status == BookCoverStatus.Selected)
            .ToListAsync(ct);
        foreach (var p in prior)
        {
            p.Status = BookCoverStatus.Ready;
            p.UpdatedDate = DateTime.UtcNow;
        }

        var cover = new BookCover
        {
            Id = Guid.NewGuid(),
            BookProjectId = projectId,
            Prompt = req.CustomQuery ?? photo.AltDescription ?? photo.Description ?? "(Unsplash)",
            Style = $"Unsplash · {photo.User.Name}",
            AssetId = assetId,
            Status = BookCoverStatus.Selected,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookCovers.Add(cover);

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            cover.Id,
            assetId,
            assetUrl = $"/api/books/{projectId}/assets/{assetId}/download",
            attribution = JsonSerializer.Deserialize<object>(attribution),
        });
    }

    [HttpPost("generate-ai")]
    public async Task<ActionResult<object>> GenerateAi(
        Guid projectId, [FromBody] GenerateAiCoverRequest req, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (!imageGen.IsEnabled)
        {
            return StatusCode(503, new
            {
                error = "No image-gen provider configured. Set OPENAI_API_KEY or STABILITY_API_KEY.",
            });
        }

        // Derive a prompt from the project context when the caller didn't supply one. The
        // typography (title/author) is overlaid by the cover-export step, so we explicitly
        // ask the model NOT to render text on the image.
        var requestedPrompt = string.IsNullOrWhiteSpace(req.Prompt)
            ? DerivePromptFromProject(project, req.Style)
            : req.Prompt!.Trim();

        var genRequest = new GenerateImageRequest(
            Prompt: requestedPrompt,
            Width: 1024,
            Height: 1792,
            StyleHint: string.IsNullOrWhiteSpace(req.Style) ? null : req.Style,
            NegativePrompt: "text, typography, letters, words, captions, watermark");

        GeneratedImage? result;
        try
        {
            result = await imageGen.GenerateAsync(genRequest, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AI cover generation threw for project {ProjectId}", projectId);
            return StatusCode(502, new { error = "Image generation failed.", detail = ex.Message });
        }

        if (result is null)
        {
            return StatusCode(502, new
            {
                error = "Image generation failed.",
                detail = $"Provider '{imageGen.ProviderName}' did not return an image. Check server logs.",
            });
        }

        var assetId = Guid.NewGuid();
        const string fileName = "cover-ai.png";
        var key = $"{cid}/{projectId}/{assetId}-{fileName}";
        using (var ms = new MemoryStream(result.PngBytes))
        {
            await storage.SaveAsync("covers", key, ms, ct);
        }

        var attribution = JsonSerializer.Serialize(new
        {
            source = result.Provider,
            modelVersion = result.ModelVersion,
            promptUsed = result.PromptUsed,
            generatedAt = DateTime.UtcNow,
        });

        var asset = new BookAsset
        {
            Id = assetId,
            BookProjectId = projectId,
            AssetType = BookAssetType.Cover,
            FileName = fileName,
            StorageProvider = StorageProvider.Local,
            BlobContainer = "covers",
            BlobKey = key,
            MimeType = "image/png",
            FileSize = result.PngBytes.LongLength,
            ScanStatus = ScanStatus.Skipped,
            AttributionJson = attribution,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookAssets.Add(asset);

        var cover = new BookCover
        {
            Id = Guid.NewGuid(),
            BookProjectId = projectId,
            Prompt = result.PromptUsed,
            Style = req.Style,
            AssetId = assetId,
            // Stays Ready (not Selected) — selecting is a separate user action so we don't
            // silently demote the cover the operator picked earlier.
            Status = BookCoverStatus.Ready,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookCovers.Add(cover);

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            id = cover.Id,
            assetId,
            status = cover.Status.ToString(),
            downloadUrl = $"/api/books/{projectId}/assets/{assetId}/download",
            attribution = JsonSerializer.Deserialize<object>(attribution),
        });
    }

    /// <summary>
    /// Default prompt template — used when the caller doesn't supply one. Concatenates the
    /// project metadata we have on hand and explicitly forbids on-image text since the
    /// title/author are overlaid by the export step.
    /// </summary>
    private static string DerivePromptFromProject(BookProject project, string? style)
    {
        var title = string.IsNullOrWhiteSpace(project.Title) ? "Untitled" : project.Title;
        var subtitleLine = string.IsNullOrWhiteSpace(project.Subtitle)
            ? string.Empty
            : $" — {project.Subtitle}";
        var genrePart = string.IsNullOrWhiteSpace(project.Genre)
            ? "General fiction"
            : project.Genre!;
        var tonePart = string.IsNullOrWhiteSpace(project.WritingTone)
            ? "evocative"
            : project.WritingTone!;
        var audienceLine = string.IsNullOrWhiteSpace(project.TargetAudience)
            ? string.Empty
            : $" Audience: {project.TargetAudience}.";
        var stylePart = string.IsNullOrWhiteSpace(style)
            ? "cinematic, professional, high-detail, well-composed for portrait aspect"
            : style!;

        return
            $"Book cover art for '{title}'{subtitleLine}. {genrePart}. Tone: {tonePart}.{audienceLine} " +
            $"Style: {stylePart}. No text or typography on the image.";
    }

    [HttpDelete("{coverId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid coverId, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var cover = await db.BookCovers
            .Where(c => c.Id == coverId && c.BookProjectId == projectId)
            .Join(db.BookProjects,
                c => c.BookProjectId, p => p.Id,
                (c, p) => new { cover = c, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .Select(x => x.cover)
            .FirstOrDefaultAsync(ct);
        if (cover is null) return NotFound();

        db.BookCovers.Remove(cover);
        if (cover.AssetId is { } aid)
        {
            var asset = await db.BookAssets.FirstOrDefaultAsync(a => a.Id == aid, ct);
            if (asset is not null)
            {
                await storage.DeleteAsync(asset.BlobContainer, asset.BlobKey, ct);
                db.BookAssets.Remove(asset);
            }
        }
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
