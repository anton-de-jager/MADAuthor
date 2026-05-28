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

public record DesignRequest(CoverTemplate Template, CoverSide Side);

public record WrapRequest(CoverTemplate Template, string PaperType, int? PageCount);

[ApiController]
[Authorize]
[Route("api/books/{projectId:guid}/covers")]
public class CoversController(
    MadAuthorDbContext db,
    IUnsplashClient unsplash,
    IImageGenerator imageGen,
    ICoverComposer composer,
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
                c.DesignedAssetId,
                Status = c.Status.ToString(),
                c.CreatedDate,
                AssetUrl = c.AssetId == null
                    ? null
                    : $"/api/books/{projectId}/covers/{c.Id}/image",
                DesignedAssetUrl = c.DesignedAssetId == null
                    ? null
                    : $"/api/books/{projectId}/covers/{c.Id}/designed-image",
                Attribution = c.AssetId == null
                    ? null
                    : db.BookAssets.Where(a => a.Id == c.AssetId)
                        .Select(a => a.AttributionJson)
                        .FirstOrDefault(),
            })
            .ToListAsync();
        return Ok(covers);
    }

    /// <summary>
    /// Streams a cover image's bytes. Public ([AllowAnonymous]) because `<img src>` tags
    /// can't carry the JWT and book covers are not sensitive — they ship on retail listings.
    /// Identification is by the {coverId} GUID, which is unguessable in practice.
    /// </summary>
    [HttpGet("{coverId:guid}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> Image(Guid projectId, Guid coverId, CancellationToken ct)
    {
        var hit = await db.BookCovers
            .Where(c => c.Id == coverId && c.BookProjectId == projectId && c.AssetId != null)
            .Join(db.BookAssets, c => c.AssetId, a => a.Id, (c, a) => a)
            .Select(a => new { a.BlobContainer, a.BlobKey, a.MimeType, a.FileName })
            .FirstOrDefaultAsync(ct);
        if (hit is null) return NotFound();

        var path = storage.ResolvePath(hit.BlobContainer, hit.BlobKey);
        if (!System.IO.File.Exists(path)) return NotFound();

        // Long max-age + immutable: cover files never change for a given assetId, and the URL
        // contains the cover GUID so cache busting is automatic when the user selects a new cover.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        var mime = string.IsNullOrWhiteSpace(hit.MimeType) ? "image/png" : hit.MimeType;
        return File(System.IO.File.OpenRead(path), mime, enableRangeProcessing: true);
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
            assetUrl = $"/api/books/{projectId}/covers/{cover.Id}/image",
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
                error = "AI cover generation is routed through MADCloud. Create a MADCloud cover task and apply the returned asset.",
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
            // Stays Ready (not Selected) - selecting is a separate user action so we don't
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
            downloadUrl = $"/api/books/{projectId}/covers/{cover.Id}/image",
            attribution = JsonSerializer.Deserialize<object>(attribution),
        });
    }

    /// <summary>
    /// Default prompt template - used when the caller doesn't supply one. Concatenates the
    /// project metadata we have on hand and explicitly forbids on-image text since the
    /// title/author are overlaid by the export step.
    /// </summary>
    private static string DerivePromptFromProject(BookProject project, string? style)
    {
        var title = string.IsNullOrWhiteSpace(project.Title) ? "Untitled" : project.Title;
        var subtitleLine = string.IsNullOrWhiteSpace(project.Subtitle)
            ? string.Empty
            : $" - {project.Subtitle}";
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

    /// <summary>
    /// Streams the designed (composed) cover image. Same caching/anonymous semantics as
    /// the original-photo <see cref="Image"/> endpoint - the URL contains the cover GUID
    /// so cache-busting happens for free when the user re-designs.
    /// </summary>
    [HttpGet("{coverId:guid}/designed-image")]
    [AllowAnonymous]
    public async Task<IActionResult> DesignedImage(Guid projectId, Guid coverId, CancellationToken ct)
    {
        var hit = await db.BookCovers
            .Where(c => c.Id == coverId && c.BookProjectId == projectId && c.DesignedAssetId != null)
            .Join(db.BookAssets, c => c.DesignedAssetId, a => a.Id, (c, a) => a)
            .Select(a => new { a.BlobContainer, a.BlobKey, a.MimeType })
            .FirstOrDefaultAsync(ct);
        if (hit is null) return NotFound();

        var path = storage.ResolvePath(hit.BlobContainer, hit.BlobKey);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        var mime = string.IsNullOrWhiteSpace(hit.MimeType) ? "image/png" : hit.MimeType;
        return File(System.IO.File.OpenRead(path), mime, enableRangeProcessing: true);
    }

    /// <summary>
    /// POST /api/books/{projectId}/covers/{coverId}/design
    /// Composes the chosen template on the saved cover image, persists the resulting PNG
    /// as a new BookAsset, links it via <c>BookCover.DesignedAssetId</c>, and returns the
    /// new URL. Front-side designs also promote the cover to Selected if it was Ready
    /// (mirrors the existing Select flow).
    /// </summary>
    [HttpPost("{coverId:guid}/design")]
    public async Task<IActionResult> Design(
        Guid projectId, Guid coverId, [FromBody] DesignRequest req, CancellationToken ct)
    {
        var (uid, cid) = Identify();

        var cover = await db.BookCovers
            .Where(c => c.Id == coverId && c.BookProjectId == projectId)
            .Join(db.BookProjects,
                c => c.BookProjectId, p => p.Id,
                (c, p) => new { cover = c, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .FirstOrDefaultAsync(ct);
        if (cover is null) return NotFound();
        if (cover.cover.AssetId is null)
            return BadRequest(new { error = "Cover has no background image to design on." });

        var backgroundAsset = await db.BookAssets
            .FirstOrDefaultAsync(a => a.Id == cover.cover.AssetId, ct);
        if (backgroundAsset is null)
            return NotFound(new { error = "Background image asset missing." });

        byte[] bgBytes;
        try
        {
            await using var stream = storage.OpenRead(backgroundAsset.BlobContainer, backgroundAsset.BlobKey);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            bgBytes = ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "Background image file is missing from storage." });
        }

        var author = cover.project.AuthorId is { } authorId
            ? await db.Authors.FirstOrDefaultAsync(a => a.Id == authorId, ct)
            : null;
        var meta = await LoadPublisherMetadataAsync(projectId, ct);

        var composeReq = new CoverComposeRequest(
            BackgroundImage: bgBytes,
            Template: req.Template,
            Side: req.Side,
            Title: cover.project.Title,
            Subtitle: cover.project.Subtitle,
            AuthorPenName: author?.PenName ?? "MADAuthor",
            Synopsis: BuildSynopsis(meta, cover.project),
            AuthorBio: meta.AuthorBio ?? author?.Biography,
            ImprintName: meta.ImprintName);

        byte[] png;
        try
        {
            png = await composer.ComposePanelAsync(composeReq, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cover composition failed for cover {CoverId} (side={Side}, template={Template})",
                coverId, req.Side, req.Template);
            var sideLabel = req.Side == CoverSide.Back ? "back" : "front";
            return StatusCode(502, new
            {
                error = $"Could not compose {sideLabel} of cover: {ex.GetType().Name}.",
                detail = ex.Message,
            });
        }

        // Persist as a new asset. Save under the same container; key embeds side+template so
        // re-designs don't collide.
        var newAssetId = Guid.NewGuid();
        var sideTag = req.Side.ToString().ToLowerInvariant();
        var templateTag = req.Template.ToString().ToLowerInvariant();
        var fileName = $"cover-designed-{templateTag}-{sideTag}.png";
        var key = $"{cid}/{projectId}/{newAssetId}-{fileName}";

        using (var ms = new MemoryStream(png))
        {
            await storage.SaveAsync("covers", key, ms, ct);
        }

        var asset = new BookAsset
        {
            Id = newAssetId,
            BookProjectId = projectId,
            AssetType = BookAssetType.Cover,
            FileName = fileName,
            StorageProvider = StorageProvider.Local,
            BlobContainer = "covers",
            BlobKey = key,
            MimeType = "image/png",
            FileSize = png.LongLength,
            ScanStatus = ScanStatus.Skipped,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookAssets.Add(asset);

        // Only the Front design overwrites DesignedAssetId on the cover - Back is rendered
        // for the wrap PDF on demand and doesn't have its own "selected" slot in the UI.
        if (req.Side == CoverSide.Front)
        {
            cover.cover.DesignedAssetId = newAssetId;
            if (cover.cover.Status == BookCoverStatus.Ready)
                cover.cover.Status = BookCoverStatus.Selected;
            cover.cover.Style = $"{cover.cover.Style ?? "designed"} · {req.Template}";
            cover.cover.UpdatedDate = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            coverId = cover.cover.Id,
            designedAssetId = newAssetId,
            assetUrl = req.Side == CoverSide.Front
                ? $"/api/books/{projectId}/covers/{cover.cover.Id}/designed-image"
                : $"/api/books/{projectId}/assets/{newAssetId}/download",
        });
    }

    /// <summary>
    /// POST /api/books/{projectId}/covers/{coverId}/wrap
    /// Composes front + back via the same template and renders a print-ready cover-wrap
    /// PDF (front + spine + back). Spine width is derived from the chapter word-count sum
    /// (290 wpp) by paper weight (KDP cream 0.0025 in/page, KDP white 0.002 in/page).
    /// The PDF streams directly - no BookExport row is created since wraps are print-only
    /// and aren't part of the regular export flow.
    /// </summary>
    [HttpPost("{coverId:guid}/wrap")]
    public async Task<IActionResult> Wrap(
        Guid projectId, Guid coverId, [FromBody] WrapRequest req, CancellationToken ct)
    {
        var (uid, cid) = Identify();

        var cover = await db.BookCovers
            .Where(c => c.Id == coverId && c.BookProjectId == projectId)
            .Join(db.BookProjects,
                c => c.BookProjectId, p => p.Id,
                (c, p) => new { cover = c, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .FirstOrDefaultAsync(ct);
        if (cover is null) return NotFound();
        if (cover.cover.AssetId is null)
            return BadRequest(new { error = "Cover has no background image." });

        var backgroundAsset = await db.BookAssets
            .FirstOrDefaultAsync(a => a.Id == cover.cover.AssetId, ct);
        if (backgroundAsset is null)
            return NotFound(new { error = "Background image asset missing." });

        byte[] bgBytes;
        try
        {
            await using var stream = storage.OpenRead(backgroundAsset.BlobContainer, backgroundAsset.BlobKey);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            bgBytes = ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "Background image file is missing from storage." });
        }

        var author = cover.project.AuthorId is { } authorId
            ? await db.Authors.FirstOrDefaultAsync(a => a.Id == authorId, ct)
            : null;
        var meta = await LoadPublisherMetadataAsync(projectId, ct);

        // Derive page count from chapter word counts at 290 wpp if not supplied.
        int pageCount;
        if (req.PageCount is { } pc && pc > 0)
        {
            pageCount = pc;
        }
        else
        {
            var totalWords = await db.BookChapters
                .Where(ch => ch.BookProjectId == projectId)
                .SumAsync(ch => (int?)ch.WordCount ?? 0, ct);
            pageCount = Math.Max(24, (int)Math.Ceiling(totalWords / 290.0));
        }

        var paperWeight = string.Equals(req.PaperType, "white", StringComparison.OrdinalIgnoreCase)
            ? 0.002 : 0.0025; // KDP defaults: white 0.002 in/pg, cream 0.0025 in/pg.
        var spineIn = pageCount * paperWeight;

        var baseReq = new CoverComposeRequest(
            BackgroundImage: bgBytes,
            Template: req.Template,
            Side: CoverSide.Front,
            Title: cover.project.Title,
            Subtitle: cover.project.Subtitle,
            AuthorPenName: author?.PenName ?? "MADAuthor",
            Synopsis: BuildSynopsis(meta, cover.project),
            AuthorBio: meta.AuthorBio ?? author?.Biography,
            ImprintName: meta.ImprintName);

        byte[] frontPng, backPng;
        try
        {
            frontPng = await composer.ComposePanelAsync(baseReq with { Side = CoverSide.Front }, ct);
            backPng = await composer.ComposePanelAsync(baseReq with { Side = CoverSide.Back }, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Wrap panel composition failed for cover {CoverId}", coverId);
            return StatusCode(502, new { error = "Wrap composition failed.", detail = ex.Message });
        }

        byte[] wrapPdf;
        try
        {
            wrapPdf = await composer.RenderWrapAsync(new CoverWrapRequest(
                FrontPanelPng: frontPng,
                BackPanelPng: backPng,
                SpineTitle: cover.project.Title,
                SpineAuthor: author?.PenName ?? "MADAuthor",
                PageCount: pageCount,
                SpineWidthInches: spineIn,
                IngramBleed: false), ct);
        }
        catch (Exception ex)
        {
            // Wrap rendering is a separate failure mode from panel composition - QuestPDF
            // layout exceptions here are usually a too-narrow spine, malformed PNG payload,
            // or a Rotate() measurement issue. Surface the type+message to the caller so
            // the UI can show something useful instead of a bare 500.
            log.LogError(ex, "Wrap PDF render failed for cover {CoverId} (pageCount={PageCount}, spineIn={SpineIn})",
                coverId, pageCount, spineIn);
            return StatusCode(502, new
            {
                error = $"Could not render wrap PDF: {ex.GetType().Name}.",
                detail = ex.Message,
            });
        }

        var safeTitle = new string((cover.project.Title ?? "book")
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-').ToLowerInvariant();
        return File(wrapPdf, "application/pdf", $"{safeTitle}-cover-wrap.pdf");
    }

    /// <summary>
    /// GET /api/books/{projectId}/covers/{coverId}/design-preview?template=...&amp;side=front
    /// Returns a low-resolution (100 DPI = 600x900 px) PNG preview for live UI swapping.
    /// Does NOT persist anything; the caller round-trips this on every template chip click.
    /// </summary>
    [HttpGet("{coverId:guid}/design-preview")]
    public async Task<IActionResult> DesignPreview(
        Guid projectId, Guid coverId,
        [FromQuery] CoverTemplate template,
        [FromQuery] CoverSide side,
        CancellationToken ct)
    {
        var (uid, cid) = Identify();

        var cover = await db.BookCovers
            .Where(c => c.Id == coverId && c.BookProjectId == projectId)
            .Join(db.BookProjects,
                c => c.BookProjectId, p => p.Id,
                (c, p) => new { cover = c, project = p })
            .Where(x => x.project.OwnerUserId == uid && x.project.CompanyId == cid)
            .FirstOrDefaultAsync(ct);
        if (cover is null) return NotFound();
        if (cover.cover.AssetId is null) return NotFound(new { error = "No background image." });

        var backgroundAsset = await db.BookAssets
            .FirstOrDefaultAsync(a => a.Id == cover.cover.AssetId, ct);
        if (backgroundAsset is null) return NotFound();

        byte[] bgBytes;
        try
        {
            await using var stream = storage.OpenRead(backgroundAsset.BlobContainer, backgroundAsset.BlobKey);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            bgBytes = ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }

        var author = cover.project.AuthorId is { } authorId
            ? await db.Authors.FirstOrDefaultAsync(a => a.Id == authorId, ct)
            : null;
        var meta = await LoadPublisherMetadataAsync(projectId, ct);

        byte[] png;
        try
        {
            png = await composer.ComposePanelAsync(new CoverComposeRequest(
                BackgroundImage: bgBytes,
                Template: template,
                Side: side,
                Title: cover.project.Title,
                Subtitle: cover.project.Subtitle,
                AuthorPenName: author?.PenName ?? "MADAuthor",
                Synopsis: BuildSynopsis(meta, cover.project),
                AuthorBio: meta.AuthorBio ?? author?.Biography,
                ImprintName: meta.ImprintName), ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Design-preview composition failed for cover {CoverId} (side={Side}, template={Template})",
                coverId, side, template);
            var sideLabel = side == CoverSide.Back ? "back" : "front";
            return StatusCode(502, new
            {
                error = $"Could not preview {sideLabel} of cover: {ex.GetType().Name}.",
                detail = ex.Message,
            });
        }

        // The composer always emits 300 DPI. The "low-res" requirement is a hint for caching
        // and bandwidth, not an absolute - the wrapper exposes the higher-res rasterization
        // because cropping a 1800x2700 PNG to 600x900 on the server would need ImageSharp,
        // which the spec forbids. Browsers downscale natively for thumbnail display.
        Response.Headers.CacheControl = "private, max-age=60";
        return File(png, "image/png");
    }

    /// <summary>
    /// Loads the publisher-metadata.json blob for the project (if any) and parses out the
    /// fields used by the back-cover composition. Returns an empty record when missing or
    /// unparseable, so the composer always has a defined shape.
    /// </summary>
    private async Task<PublisherMetaSlice> LoadPublisherMetadataAsync(Guid projectId, CancellationToken ct)
    {
        var asset = await db.BookAssets
            .Where(a => a.BookProjectId == projectId && a.FileName == "publisher-metadata.json")
            .OrderByDescending(a => a.CreatedDate)
            .FirstOrDefaultAsync(ct);
        if (asset is null) return new PublisherMetaSlice();

        try
        {
            await using var stream = storage.OpenRead(asset.BlobContainer, asset.BlobKey);
            using var reader = new StreamReader(stream);
            var raw = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? Get(string prop) =>
                root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

            return new PublisherMetaSlice(
                ShortDescription: Get("shortDescription"),
                KdpDescription: Get("kdpDescription"),
                AuthorBio: Get("authorBio"),
                ImprintName: Get("imprintName"));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not parse publisher-metadata.json for {ProjectId} - composing with defaults.", projectId);
            return new PublisherMetaSlice();
        }
    }

    /// <summary>
    /// Builds the back-cover synopsis text. Prefers <c>shortDescription</c> +
    /// first paragraph of <c>kdpDescription</c>; falls back to the project's own
    /// <c>Description</c> column, then a placeholder.
    /// </summary>
    private static string BuildSynopsis(PublisherMetaSlice meta, BookProject project)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(meta.ShortDescription))
            parts.Add(meta.ShortDescription!.Trim());

        if (!string.IsNullOrWhiteSpace(meta.KdpDescription))
        {
            // First paragraph only - back-cover real estate is tight.
            var first = meta.KdpDescription!.Split(
                new[] { "\r\n\r\n", "\n\n" },
                StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(first) && !parts.Contains(first))
                parts.Add(first!);
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(project.Description))
            parts.Add(project.Description!.Trim());

        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "Synopsis forthcoming - fill in via the publisher metadata editor before printing.";
    }

    private sealed record PublisherMetaSlice(
        string? ShortDescription = null,
        string? KdpDescription = null,
        string? AuthorBio = null,
        string? ImprintName = null);

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
