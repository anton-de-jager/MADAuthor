using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MadAuthor.Application.Auth;
using MadAuthor.Application.Storage;
using MadAuthor.Application.Translation;
using MadAuthor.Domain.Entities;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

/// <summary>
/// Translate finalized chapter content into another language. The result of each translation is
/// stored as a <see cref="BookAssetType.Generated"/> BookAsset attached to the original
/// BookProject - we deliberately do NOT clone the BookProject. The UI surfaces these assets as
/// downloadable Markdown files in the Publishing tab.
/// </summary>
[ApiController]
[Authorize]
[Route("api/books/{projectId:guid}/translate")]
public class TranslationController(
    MadAuthorDbContext db,
    IFileStorage storage,
    ITranslator translator,
    ICurrentUserService currentUser,
    ILogger<TranslationController> log) : ControllerBase
{
    // Safety cap: refuse to translate more chapters than this in one call. Translation is slow
    // (single-digit minutes per chapter on gpt-4o-mini) and expensive; if a user has more than
    // 30 final chapters they should translate in batches via the per-chapter endpoint.
    private const int MaxChaptersPerBookCall = 30;

    /// <summary>Translate a single Final chapter. Stored as a Generated BookAsset.</summary>
    [HttpPost("{chapterId:guid}")]
    public async Task<IActionResult> TranslateChapter(
        Guid projectId, Guid chapterId,
        [FromQuery] string language,
        [FromQuery] string? style,
        CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (string.IsNullOrWhiteSpace(language))
            return BadRequest(new { error = "Query parameter 'language' is required." });
        if (LooksLikeSameLanguage(project.Language, language))
            return BadRequest(new { error = "Target language matches the source language; nothing to translate." });
        if (!translator.IsEnabled)
            return StatusCode(503, new { error = "No translation provider configured. Set OPENAI_API_KEY or DEEPL_API_KEY on the API and restart." });

        var chapter = await db.BookChapters
            .FirstOrDefaultAsync(c => c.Id == chapterId && c.BookProjectId == projectId, ct);
        if (chapter is null) return NotFound(new { error = "Chapter not found." });
        if (chapter.Status != BookChapterStatus.Final)
            return BadRequest(new { error = $"Chapter is not Final (status: {chapter.Status})." });
        if (string.IsNullOrWhiteSpace(chapter.ContentMarkdown))
            return BadRequest(new { error = "Chapter has no Markdown content to translate." });

        var result = await TranslateAndPersistAsync(
            project, chapter, language, style, ct);
        if (result is null)
            return StatusCode(502, new { error = "Translation provider returned no result. Check the server logs." });

        return Ok(new
        {
            assetId = result.AssetId,
            chapterId = chapter.Id,
            chapterNumber = chapter.ChapterNumber,
            downloadUrl = $"/api/books/{projectId}/assets/{result.AssetId}/download",
            provider = result.Provider,
            targetLanguage = language,
            sourceLanguage = result.SourceLanguageDetected,
            fileName = result.FileName,
        });
    }

    /// <summary>Translate every Final chapter in order. Returns one entry per translated chapter.</summary>
    [HttpPost("")]
    public async Task<IActionResult> TranslateBook(
        Guid projectId,
        [FromQuery] string language,
        [FromQuery] string? style,
        CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (string.IsNullOrWhiteSpace(language))
            return BadRequest(new { error = "Query parameter 'language' is required." });
        if (LooksLikeSameLanguage(project.Language, language))
            return BadRequest(new { error = "Target language matches the source language; nothing to translate." });
        if (!translator.IsEnabled)
            return StatusCode(503, new { error = "No translation provider configured. Set OPENAI_API_KEY or DEEPL_API_KEY on the API and restart." });

        var chapters = await db.BookChapters
            .Where(c => c.BookProjectId == projectId && c.Status == BookChapterStatus.Final)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(ct);
        if (chapters.Count == 0)
            return BadRequest(new { error = "No Final chapters to translate." });
        if (chapters.Count > MaxChaptersPerBookCall)
            return BadRequest(new
            {
                error = $"Too many chapters ({chapters.Count}). The book-translate endpoint is capped at " +
                        $"{MaxChaptersPerBookCall}; translate in smaller batches via the per-chapter endpoint.",
            });

        var results = new List<object>();
        foreach (var chapter in chapters)
        {
            if (string.IsNullOrWhiteSpace(chapter.ContentMarkdown))
            {
                log.LogInformation("Skipping chapter {ChapterId} (no content) during book translation.", chapter.Id);
                continue;
            }

            var translated = await TranslateAndPersistAsync(project, chapter, language, style, ct);
            if (translated is null)
            {
                results.Add(new
                {
                    chapterId = chapter.Id,
                    chapterNumber = chapter.ChapterNumber,
                    error = "Translation provider returned no result.",
                });
                continue;
            }

            results.Add(new
            {
                assetId = translated.AssetId,
                chapterId = chapter.Id,
                chapterNumber = chapter.ChapterNumber,
                downloadUrl = $"/api/books/{projectId}/assets/{translated.AssetId}/download",
                provider = translated.Provider,
                sourceLanguage = translated.SourceLanguageDetected,
                fileName = translated.FileName,
            });
        }

        return Ok(new
        {
            projectId,
            targetLanguage = language,
            provider = translator.ProviderName,
            chaptersTranslated = results.Count,
            results,
        });
    }

    private async Task<PersistedTranslation?> TranslateAndPersistAsync(
        BookProject project, BookChapter chapter, string targetLanguage, string? style, CancellationToken ct)
    {
        var translation = await translator.TranslateAsync(
            new TranslateRequest(
                SourceMarkdown: chapter.ContentMarkdown ?? string.Empty,
                TargetLanguage: targetLanguage,
                SourceLanguage: project.Language,
                StyleHint: style),
            ct);
        if (translation is null || string.IsNullOrWhiteSpace(translation.TranslatedMarkdown))
            return null;

        var assetId = Guid.NewGuid();
        var langSlug = Slug(targetLanguage);
        var titleSlug = Slug(chapter.Title);
        var fileName = $"ch{chapter.ChapterNumber:00}-{titleSlug}-{langSlug}.md";
        var blobKey = $"{project.CompanyId}/{project.Id}/{assetId}-translated-ch{chapter.ChapterNumber}-{langSlug}.md";

        var bytes = Encoding.UTF8.GetBytes(translation.TranslatedMarkdown);
        using (var ms = new MemoryStream(bytes))
        {
            await storage.SaveAsync("generated", blobKey, ms, ct);
        }

        var attribution = JsonSerializer.Serialize(new
        {
            source = translation.Provider,
            sourceLanguage = translation.SourceLanguageDetected,
            targetLanguage,
            chapterId = chapter.Id,
            chapterNumber = chapter.ChapterNumber,
            style,
        });

        var asset = new BookAsset
        {
            Id = assetId,
            BookProjectId = project.Id,
            AssetType = BookAssetType.Generated,
            FileName = fileName,
            StorageProvider = StorageProvider.Local,
            BlobContainer = "generated",
            BlobKey = blobKey,
            MimeType = "text/markdown",
            FileSize = bytes.LongLength,
            ScanStatus = ScanStatus.Skipped,
            AttributionJson = attribution,
            CreatedDate = DateTime.UtcNow,
        };
        db.BookAssets.Add(asset);
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Translated chapter {ChapterId} (ch{Number}) into {Target} via {Provider}; saved asset {AssetId}.",
            chapter.Id, chapter.ChapterNumber, targetLanguage, translation.Provider, assetId);

        return new PersistedTranslation(
            AssetId: assetId,
            FileName: fileName,
            Provider: translation.Provider,
            SourceLanguageDetected: translation.SourceLanguageDetected);
    }

    /// <summary>True when the target language obviously matches the source. We're lenient - "en"
    /// vs "English" both match an "en" source - so a typo doesn't accidentally translate
    /// English-to-English and burn a paid API call.</summary>
    private static bool LooksLikeSameLanguage(string source, string target)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) return false;
        var s = source.Trim().ToLowerInvariant();
        var t = target.Trim().ToLowerInvariant();
        if (s == t) return true;

        // Map common name <-> code pairs so "English" matches "en".
        string Normalize(string v) => v switch
        {
            "english" => "en",
            "spanish" or "espanol" or "español" => "es",
            "french" or "français" or "francais" => "fr",
            "german" or "deutsch" => "de",
            "portuguese" => "pt",
            "italian" or "italiano" => "it",
            "dutch" or "nederlands" => "nl",
            "polish" or "polski" => "pl",
            "russian" => "ru",
            "japanese" => "ja",
            "chinese" or "mandarin" or "mandarin chinese" => "zh",
            "korean" => "ko",
            "turkish" => "tr",
            "arabic" => "ar",
            "afrikaans" => "af",
            _ => v.Length >= 2 ? v[..2] : v,
        };
        return Normalize(s) == Normalize(t);
    }

    private static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "untitled";
        var lower = value.Trim().ToLowerInvariant();
        var ascii = Regex.Replace(lower, @"[^a-z0-9\s-]", string.Empty);
        var collapsed = Regex.Replace(ascii, @"[\s-]+", "-").Trim('-');
        if (string.IsNullOrEmpty(collapsed)) return "untitled";
        return collapsed.Length > 60 ? collapsed[..60].TrimEnd('-') : collapsed;
    }

    private (Guid userId, Guid companyId) Identify()
    {
        if (currentUser.UserId is not { } uid || currentUser.CompanyId is not { } cid)
            throw new UnauthorizedAccessException();
        return (uid, cid);
    }

    private sealed record PersistedTranslation(
        Guid AssetId, string FileName, string Provider, string SourceLanguageDetected);
}
