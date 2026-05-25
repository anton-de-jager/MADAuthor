using System.Text.Json;
using MadAuthor.Application.Exports;
using MadAuthor.Application.Storage;
using MadAuthor.Domain.Enums;
using MadAuthor.Infrastructure.Exports;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Realtime;

/// <summary>
/// Background renderer: picks up <c>BookExports</c> rows with Status=Queued, dispatches to
/// the format-specific renderer (PDF/DOCX/EPUB), stores the bytes via <see cref="IFileStorage"/>,
/// and flips the row to Ready. Loads the project's Selected cover and embeds it in the export.
/// </summary>
public class ExportRendererService(
    IServiceScopeFactory scopes,
    ILogger<ExportRendererService> log) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await Tick(ct); }
            catch (Exception ex) { log.LogWarning(ex, "ExportRendererService tick failed."); }
            try { await Task.Delay(PollInterval, ct); } catch { return; }
        }
    }

    private async Task Tick(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MadAuthorDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        // Heal stuck-in-Running rows. An IIS recycle mid-render leaves a row
        // in `Status=Running` forever; the next process can't re-claim it
        // because our claim filter is `Status=Queued`. If a row has been Running
        // longer than the longest plausible render (5 minutes), assume the worker
        // that owned it is dead and put the row back in the queue.
        var stuckThreshold = DateTime.UtcNow.AddMinutes(-5);
        var stuck = await db.BookExports
            .Where(e => e.Status == BookExportStatus.Running
                     && e.UpdatedDate != null && e.UpdatedDate < stuckThreshold)
            .ToListAsync(ct);
        if (stuck.Count > 0)
        {
            foreach (var e in stuck)
            {
                log.LogWarning("Requeuing stuck export {ExportId} (was Running since {When})",
                    e.Id, e.UpdatedDate);
                e.Status = BookExportStatus.Queued;
                e.UpdatedDate = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }

        var queued = await db.BookExports
            .Where(e => e.Status == BookExportStatus.Queued)
            .OrderBy(e => e.CreatedDate)
            .Take(5)
            .ToListAsync(ct);

        foreach (var export in queued)
        {
            try
            {
                export.Status = BookExportStatus.Running;
                export.UpdatedDate = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                var project = await db.BookProjects.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == export.BookProjectId, ct);
                if (project is null) throw new InvalidOperationException("BookProject not found.");

                var author = project.AuthorId is { } aid
                    ? await db.Authors.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == aid, ct)
                    : null;

                var chapters = await db.BookChapters.IgnoreQueryFilters()
                    .Where(c => c.BookProjectId == project.Id
                                && (c.Status == BookChapterStatus.Final
                                    || c.Status == BookChapterStatus.Drafted))
                    .OrderBy(c => c.ChapterNumber)
                    .Select(c => new ExportChapter(c.ChapterNumber, c.Title, c.ContentMarkdown ?? string.Empty))
                    .ToListAsync(ct);

                if (chapters.Count == 0)
                    throw new InvalidOperationException("No chapter content available to export.");

                // Load the most recently Selected cover, if any, and embed.
                // Prefer the DESIGNED asset (typography overlaid by the cover composer)
                // over the raw background photo. The flag tells the renderer not to
                // double-up its own title/subtitle/author text block on top.
                ExportCoverImage? cover = null;
                var selectedCover = await db.BookCovers.IgnoreQueryFilters()
                    .Where(c => c.BookProjectId == project.Id && c.Status == BookCoverStatus.Selected)
                    .OrderByDescending(c => c.CreatedDate)
                    .FirstOrDefaultAsync(ct);

                var preferredAssetId = selectedCover?.DesignedAssetId ?? selectedCover?.AssetId;
                var isDesigned = selectedCover?.DesignedAssetId is not null;
                if (preferredAssetId is { } coverAssetId)
                {
                    var coverAsset = await db.BookAssets.FirstOrDefaultAsync(a => a.Id == coverAssetId, ct);
                    if (coverAsset is not null)
                    {
                        var coverPath = storage.ResolvePath(coverAsset.BlobContainer, coverAsset.BlobKey);
                        if (File.Exists(coverPath))
                        {
                            var bytes = await File.ReadAllBytesAsync(coverPath, ct);
                            // Attribution lives on the ORIGINAL background asset (Unsplash/AI metadata),
                            // not the composed image - so always pull it from selectedCover.AssetId
                            // even when rendering the designed bytes.
                            string? attrText = null, attrUrl = null;
                            if (selectedCover?.AssetId is { } rawId)
                            {
                                var rawAsset = await db.BookAssets.FirstOrDefaultAsync(a => a.Id == rawId, ct);
                                if (rawAsset is not null)
                                    (attrText, attrUrl) = ParseAttribution(rawAsset.AttributionJson);
                            }
                            cover = new ExportCoverImage(bytes, coverAsset.MimeType, attrText, attrUrl, isDesigned);
                        }
                    }
                }

                var context = new ExportContext(
                    project.Id, project.Title, project.Subtitle,
                    author?.PenName, project.CopyrightText, chapters, cover,
                    BodyFont: project.BodyFont);

                IExportRenderer renderer = export.ExportType switch
                {
                    BookExportType.Pdf => new PdfExportRenderer(),
                    BookExportType.Docx => new DocxExportRenderer(),
                    BookExportType.Epub => new EpubExportRenderer(),
                    BookExportType.Html => new HtmlExportRenderer(),
                    BookExportType.Markdown => new MarkdownExportRenderer(),
                    BookExportType.PrintPdfKdp => new PrintPdfKdpExportRenderer(),
                    BookExportType.PrintPdfIngram => new PrintPdfIngramExportRenderer(),
                    _ => throw new NotSupportedException(
                        $"ExportType {export.ExportType} has no renderer registered.")
                };

                var rendered = await renderer.RenderAsync(context, ct);
                var key = $"{project.CompanyId}/{project.Id}/{export.Id}-{rendered.FileName}";
                using (var ms = new MemoryStream(rendered.Bytes))
                {
                    await storage.SaveAsync("exports", key, ms, ct);
                }

                export.BlobKey = key;
                export.FileSize = rendered.Bytes.LongLength;
                export.Status = BookExportStatus.Ready;
                export.ExpiresAt = DateTime.UtcNow.AddDays(90);
                export.UpdatedDate = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                log.LogInformation("Rendered export {ExportId} ({Type}) for project {ProjectId}: {Bytes} bytes (cover: {HasCover})",
                    export.Id, export.ExportType, export.BookProjectId, rendered.Bytes.Length, cover is not null);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Export {ExportId} failed", export.Id);
                export.Status = BookExportStatus.Failed;
                export.ErrorMessage = ex.Message;
                export.UpdatedDate = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static (string? Text, string? Url) ParseAttribution(string? attributionJson)
    {
        if (string.IsNullOrWhiteSpace(attributionJson)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(attributionJson);
            var root = doc.RootElement;
            var source = root.TryGetProperty("source", out var s) ? s.GetString() : null;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            var photoUrl = root.TryGetProperty("photoUrl", out var p) ? p.GetString() : null;

            if (string.IsNullOrEmpty(name)) return (null, null);
            var text = string.IsNullOrEmpty(source)
                ? $"Cover photo by {name}"
                : $"Cover photo by {name} on {source}";
            return (text, photoUrl ?? url);
        }
        catch
        {
            return (null, null);
        }
    }
}
