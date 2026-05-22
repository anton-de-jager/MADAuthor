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
    ExportCoverImage? Cover = null);

public record ExportChapter(int Number, string Title, string ContentMarkdown);

/// <summary>Cover image embedded into PDF/EPUB exports. Attribution rendered on the copyright page.</summary>
public record ExportCoverImage(
    byte[] Bytes,
    string ContentType,
    string? AttributionText,
    string? AttributionUrl);

public record RenderedExport(byte[] Bytes, string FileName, string ContentType);
