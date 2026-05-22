using System.Text;
using MadAuthor.Application.Exports;

namespace MadAuthor.Infrastructure.Exports;

/// <summary>
/// Concatenates every chapter's source markdown into a single .md file, prefixed with
/// YAML frontmatter (title/subtitle/author/timestamp). Each chapter is separated by an
/// h-rule. Chapter ContentMarkdown is passed through verbatim — no transformation.
/// </summary>
public class MarkdownExportRenderer : IExportRenderer
{
    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // YAML frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{Yaml(ctx.Title)}\"");
        if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
            sb.AppendLine($"subtitle: \"{Yaml(ctx.Subtitle)}\"");
        if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
            sb.AppendLine($"author: \"{Yaml(ctx.AuthorPenName)}\"");
        sb.AppendLine($"generated: \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"");
        sb.AppendLine("---");
        sb.AppendLine();

        // Title page
        sb.AppendLine($"# {ctx.Title}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
        {
            sb.AppendLine($"*{ctx.Subtitle}*");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
        {
            sb.AppendLine($"By {ctx.AuthorPenName}");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(ctx.CopyrightText))
        {
            sb.AppendLine(ctx.CopyrightText);
            sb.AppendLine();
        }
        sb.AppendLine($"© {DateTime.UtcNow.Year} {ctx.AuthorPenName ?? "Author"}. All rights reserved.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText))
        {
            sb.Append("_").Append(ctx.Cover.AttributionText).Append("_");
            if (!string.IsNullOrWhiteSpace(ctx.Cover.AttributionUrl))
                sb.Append(" — <").Append(ctx.Cover.AttributionUrl).Append(">");
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // Table of contents
        sb.AppendLine("## Contents");
        sb.AppendLine();
        foreach (var ch in ctx.Chapters)
        {
            var anchor = Slug(ch.Title);
            sb.AppendLine($"{ch.Number}. [{ch.Title}](#{anchor})");
        }
        sb.AppendLine();

        // Chapters
        foreach (var ch in ctx.Chapters)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## Chapter {ch.Number}: {ch.Title}");
            sb.AppendLine();
            sb.AppendLine(ch.ContentMarkdown ?? string.Empty);
            sb.AppendLine();
        }

        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
        var safe = SafeFile(ctx.Title);
        return Task.FromResult(new RenderedExport(bytes, $"{safe}.md", "text/markdown; charset=utf-8"));
    }

    private static string Yaml(string? s)
        => (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static string SafeFile(string title)
    {
        var s = new string((title ?? "book").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return s.Trim('-').ToLowerInvariant();
    }
}
