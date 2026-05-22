using System.Text;
using MadAuthor.Application.Exports;
using Markdig;

namespace MadAuthor.Infrastructure.Exports;

/// <summary>
/// Builds a single-file styled HTML document: title page, optional copyright section,
/// table of contents, and one &lt;article&gt; per chapter. Markdown is rendered to HTML
/// via Markdig (inline emphasis is preserved natively as &lt;strong&gt;/&lt;em&gt;).
/// Print-friendly serif typography is embedded as a &lt;style&gt; block so the file is fully
/// self-contained.
/// </summary>
public class HtmlExportRenderer : IExportRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\"/>");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        sb.AppendLine($"  <title>{Esc(ctx.Title)}</title>");
        sb.AppendLine($"  <meta name=\"author\" content=\"{Esc(ctx.AuthorPenName ?? "")}\"/>");
        sb.AppendLine($"  <meta name=\"generator\" content=\"MADAuthor\"/>");
        sb.AppendLine("  <style>");
        sb.AppendLine(Css);
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Title page
        sb.AppendLine("  <section class=\"title-page\">");
        sb.AppendLine($"    <h1 class=\"book-title\">{Esc(ctx.Title)}</h1>");
        if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
            sb.AppendLine($"    <h2 class=\"book-subtitle\">{Esc(ctx.Subtitle)}</h2>");
        if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
            sb.AppendLine($"    <p class=\"author\">by {Esc(ctx.AuthorPenName)}</p>");
        sb.AppendLine("  </section>");

        // Copyright
        sb.AppendLine("  <section class=\"copyright\">");
        if (!string.IsNullOrWhiteSpace(ctx.CopyrightText))
            sb.AppendLine($"    <p>{Esc(ctx.CopyrightText)}</p>");
        sb.AppendLine($"    <p>&copy; {DateTime.UtcNow.Year} {Esc(ctx.AuthorPenName ?? "Author")}. All rights reserved.</p>");
        if (!string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText))
        {
            sb.AppendLine($"    <p class=\"attribution\">{Esc(ctx.Cover.AttributionText)}");
            if (!string.IsNullOrWhiteSpace(ctx.Cover.AttributionUrl))
                sb.AppendLine($"      <br/><a href=\"{Esc(ctx.Cover.AttributionUrl)}\">{Esc(ctx.Cover.AttributionUrl)}</a>");
            sb.AppendLine("    </p>");
        }
        sb.AppendLine("    <p class=\"published-with\">Published with MADAuthor.</p>");
        sb.AppendLine("  </section>");

        // Table of contents
        sb.AppendLine("  <nav class=\"toc\">");
        sb.AppendLine("    <h2>Contents</h2>");
        sb.AppendLine("    <ol>");
        foreach (var ch in ctx.Chapters)
            sb.AppendLine($"      <li><a href=\"#ch-{ch.Number}\">{Esc(ch.Title)}</a></li>");
        sb.AppendLine("    </ol>");
        sb.AppendLine("  </nav>");

        // Chapters
        foreach (var ch in ctx.Chapters)
        {
            var bodyHtml = global::Markdig.Markdown.ToHtml(ch.ContentMarkdown ?? string.Empty, Pipeline);
            sb.AppendLine($"  <article id=\"ch-{ch.Number}\" class=\"chapter\">");
            sb.AppendLine($"    <p class=\"chapter-meta\">Chapter {ch.Number}</p>");
            sb.AppendLine($"    <h1 class=\"chapter-title\">{Esc(ch.Title)}</h1>");
            sb.AppendLine(bodyHtml);
            sb.AppendLine("  </article>");
        }

        if (!string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText))
        {
            sb.AppendLine("  <footer class=\"site-footer\">");
            sb.AppendLine($"    <p>{Esc(ctx.Cover.AttributionText)}</p>");
            sb.AppendLine("  </footer>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
        var safe = SafeFile(ctx.Title);
        return Task.FromResult(new RenderedExport(bytes, $"{safe}.html", "text/html; charset=utf-8"));
    }

    private const string Css = """
        :root {
          --ink: #1a1a1a;
          --muted: #5a5a5a;
          --rule: #d0d0d0;
          --accent: #6b4f9b;
        }
        * { box-sizing: border-box; }
        body {
          font-family: Georgia, "Times New Roman", serif;
          font-size: 12pt;
          line-height: 1.6;
          color: var(--ink);
          max-width: 38em;
          margin: 2em auto;
          padding: 0 1.5em;
          background: #fff;
        }
        h1, h2, h3, h4 { font-family: Georgia, "Times New Roman", serif; line-height: 1.25; }
        h1 { font-size: 1.9em; margin-top: 1.6em; }
        h2 { font-size: 1.45em; margin-top: 1.4em; }
        h3 { font-size: 1.2em; margin-top: 1.2em; }
        p { margin: 0 0 0.9em; text-align: justify; hyphens: auto; }
        a { color: var(--accent); }
        blockquote {
          border-left: 3px solid var(--rule);
          margin: 1em 0;
          padding: 0.25em 0 0.25em 1em;
          color: var(--muted);
          font-style: italic;
        }
        code { font-family: Consolas, "Courier New", monospace; background: #f3f3f3; padding: 0 0.25em; font-size: 0.95em; }
        pre { font-family: Consolas, "Courier New", monospace; background: #f3f3f3; padding: 0.75em; overflow-x: auto; }
        .title-page {
          text-align: center;
          padding: 4em 0 5em;
          border-bottom: 1px solid var(--rule);
        }
        .title-page .book-title { font-size: 2.6em; margin: 0 0 0.3em; }
        .title-page .book-subtitle { font-size: 1.4em; font-style: italic; font-weight: normal; color: var(--muted); margin: 0 0 1.5em; }
        .title-page .author { font-size: 1.1em; }
        .copyright { font-size: 0.9em; color: var(--muted); padding: 2em 0; border-bottom: 1px solid var(--rule); }
        .copyright .attribution { font-size: 0.85em; margin-top: 1.5em; }
        .copyright .published-with { font-size: 0.85em; margin-top: 1.5em; }
        .toc { padding: 2em 0; border-bottom: 1px solid var(--rule); }
        .toc h2 { margin-top: 0; }
        .toc ol { padding-left: 1.5em; }
        .toc li { margin-bottom: 0.35em; }
        .toc a { text-decoration: none; }
        .toc a:hover { text-decoration: underline; }
        .chapter { padding: 2.5em 0; border-bottom: 1px solid var(--rule); }
        .chapter:last-of-type { border-bottom: none; }
        .chapter-meta { text-transform: uppercase; letter-spacing: 0.15em; font-size: 0.8em; color: var(--muted); margin: 0; }
        .chapter-title { margin: 0.1em 0 1.2em; font-size: 1.9em; }
        .site-footer { font-size: 0.8em; color: var(--muted); text-align: center; padding: 2em 0; }
        @media print {
          body { max-width: none; margin: 0; padding: 0; }
          .chapter { page-break-before: always; border: none; padding: 0; }
          .title-page, .copyright, .toc { page-break-after: always; }
        }
        """;

    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static string SafeFile(string title)
    {
        var s = new string((title ?? "book").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return s.Trim('-').ToLowerInvariant();
    }
}
