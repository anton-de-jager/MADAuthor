namespace MadAuthor.Application.Exports;

public interface IExportRenderer
{
    /// <summary>Render an export. Returns the bytes + suggested filename + content type.</summary>
    Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default);
}

public record ExportContext(
    Guid ProjectId,
    string Title,
    string? Subtitle,
    string? AuthorPenName,
    string? CopyrightText,
    IReadOnlyList<ExportChapter> Chapters,
    ExportCoverImage? Cover = null,
    /// <summary>
    /// Body-text font face. Null means "use the renderer's default" (Georgia). Must be a
    /// font face installed on the rendering server; QuestPDF will fall back silently to a
    /// system serif otherwise.
    /// </summary>
    string? BodyFont = null);

public record ExportChapter(int Number, string Title, string ContentMarkdown);

/// <summary>Cover image embedded into PDF/EPUB exports. Attribution rendered on the copyright page.</summary>
/// <param name="IsDesigned">
/// True when the bytes come from <c>BookCover.DesignedAssetId</c> - i.e. the cover composer
/// has already baked the title/subtitle/author typography onto the image. Renderers should
/// display it full-bleed and SUPPRESS their own title/author text block on the title page,
/// otherwise the typography stacks twice. False means the bytes are a raw background photo
/// (Unsplash or AI) and the renderer must overlay its own title block, as before.
/// </param>
public record ExportCoverImage(
    byte[] Bytes,
    string ContentType,
    string? AttributionText,
    string? AttributionUrl,
    bool IsDesigned = false);

public record RenderedExport(byte[] Bytes, string FileName, string ContentType);
