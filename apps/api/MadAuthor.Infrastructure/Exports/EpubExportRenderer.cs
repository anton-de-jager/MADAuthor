using System.IO.Compression;
using System.Text;
using MadAuthor.Application.Exports;
using Markdig;

namespace MadAuthor.Infrastructure.Exports;

/// <summary>
/// Builds an EPUB 3.0 file from scratch — chapter XHTML + OPF manifest + NCX (EPUB 2 compat)
/// + nav.xhtml (EPUB 3) zipped with the mimetype entry stored uncompressed first. Cover image
/// (when present) is included as a manifest item with the cover-image property + the EPUB 2
/// <meta name="cover"/> compatibility shim.
/// </summary>
public class EpubExportRenderer : IExportRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var bookId = $"madauthor-{ctx.ProjectId:N}";
        var safe = new string((ctx.Title ?? "book").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-').ToLowerInvariant();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. mimetype MUST be the first entry and stored uncompressed (EPUB spec).
            var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mimeEntry.Open()) using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write("application/epub+zip");

            // 2. container.xml — points readers at the OPF.
            WriteEntry(zip, "META-INF/container.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);

            // 3. stylesheet
            WriteEntry(zip, "OEBPS/styles.css",
                """
                @namespace epub "http://www.idpf.org/2007/ops";
                body { font-family: Georgia, serif; line-height: 1.6; color: #1a1a1a; margin: 1em; }
                h1 { font-size: 1.8em; margin-top: 1.5em; }
                h2 { font-size: 1.4em; margin-top: 1.2em; }
                h3 { font-size: 1.2em; }
                blockquote { border-left: 3px solid #888; margin: 1em 0; padding: 0.2em 0 0.2em 1em; color: #444; font-style: italic; }
                code { background: #f3f3f3; padding: 0 0.25em; font-family: Consolas, monospace; }
                pre { background: #f3f3f3; padding: 0.6em; overflow-x: auto; }
                .chapter-meta { color: #666; font-size: 0.85em; text-transform: uppercase; letter-spacing: 0.1em; }
                """);

            // 4. Cover image binary (if any).
            var coverExt = "jpg";
            var coverMime = "image/jpeg";
            if (ctx.Cover is not null)
            {
                coverMime = string.IsNullOrWhiteSpace(ctx.Cover.ContentType) ? "image/jpeg" : ctx.Cover.ContentType;
                coverExt = coverMime.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
                var entry = zip.CreateEntry($"OEBPS/cover.{coverExt}", CompressionLevel.NoCompression);
                using var s = entry.Open();
                s.Write(ctx.Cover.Bytes, 0, ctx.Cover.Bytes.Length);
            }

            // 5. Title + copyright pages
            WriteEntry(zip, "OEBPS/title.xhtml", BuildTitlePage(ctx, coverExt));
            WriteEntry(zip, "OEBPS/copyright.xhtml", BuildCopyrightPage(ctx));

            // 6. One XHTML per chapter
            for (var i = 0; i < ctx.Chapters.Count; i++)
            {
                var chapter = ctx.Chapters[i];
                WriteEntry(zip, $"OEBPS/chapter-{(i + 1):D3}.xhtml", BuildChapterPage(chapter));
            }

            // 7. EPUB 3 nav doc + EPUB 2 NCX (some readers still demand it)
            WriteEntry(zip, "OEBPS/nav.xhtml", BuildNavDoc(ctx));
            WriteEntry(zip, "OEBPS/toc.ncx", BuildNcx(bookId, ctx));

            // 8. OPF manifest + spine (cover-aware)
            WriteEntry(zip, "OEBPS/content.opf", BuildOpf(bookId, ctx, coverExt, coverMime));
        }

        var bytes = ms.ToArray();
        return Task.FromResult(new RenderedExport(bytes, $"{safe}.epub", "application/epub+zip"));
    }

    // ----- pages ------------------------------------------------------------

    private static string BuildTitlePage(ExportContext ctx, string coverExt)
    {
        var coverImg = ctx.Cover is null
            ? ""
            : $"<div style=\"text-align:center; margin-bottom:1.5em;\"><img src=\"cover.{coverExt}\" alt=\"Cover\" style=\"max-width:100%; height:auto;\"/></div>";
        return Xhtml($"""
            {coverImg}
            <h1 style="text-align:center; margin-top:{(ctx.Cover is null ? "30%" : "1em")};">{Esc(ctx.Title)}</h1>
            {(string.IsNullOrWhiteSpace(ctx.Subtitle) ? "" : $"<p style=\"text-align:center; font-style:italic;\">{Esc(ctx.Subtitle)}</p>")}
            {(string.IsNullOrWhiteSpace(ctx.AuthorPenName) ? "" : $"<p style=\"text-align:center; margin-top:2em;\">by {Esc(ctx.AuthorPenName)}</p>")}
            """, ctx.Title ?? "Title");
    }

    private static string BuildCopyrightPage(ExportContext ctx)
    {
        var attribution = string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText)
            ? ""
            : $"""
              <p style="margin-top:2em; font-size:0.85em; color:#444;">
                {Esc(ctx.Cover.AttributionText)}
                {(string.IsNullOrWhiteSpace(ctx.Cover.AttributionUrl) ? "" : $"<br/><a href=\"{Esc(ctx.Cover.AttributionUrl)}\">{Esc(ctx.Cover.AttributionUrl)}</a>")}
              </p>
              """;
        return Xhtml($"""
            <p style="margin-top:60%;">
              {(string.IsNullOrWhiteSpace(ctx.CopyrightText) ? "" : Esc(ctx.CopyrightText) + "<br/>")}
              © {DateTime.UtcNow.Year} {Esc(ctx.AuthorPenName ?? "Author")}. All rights reserved.
            </p>
            {attribution}
            <p style="font-size:0.85em; color:#666;">Published with MADAuthor.</p>
            """, "Copyright");
    }

    private static string BuildChapterPage(ExportChapter ch)
    {
        var html = global::Markdig.Markdown.ToHtml(ch.ContentMarkdown ?? string.Empty, Pipeline);
        return Xhtml($"""
            <p class="chapter-meta">Chapter {ch.Number}</p>
            {html}
            """, $"Chapter {ch.Number} — {ch.Title}");
    }

    private static string BuildNavDoc(ExportContext ctx)
    {
        var items = string.Join("\n", ctx.Chapters.Select((c, i) =>
            $"      <li><a href=\"chapter-{i + 1:D3}.xhtml\">{Esc(c.Title)}</a></li>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><meta charset="UTF-8"/><title>Contents</title><link rel="stylesheet" href="styles.css"/></head>
            <body>
              <nav epub:type="toc"><h1>Contents</h1>
                <ol>
            {items}
                </ol>
              </nav>
            </body>
            </html>
            """;
    }

    private static string BuildNcx(string bookId, ExportContext ctx)
    {
        var items = string.Join("\n", ctx.Chapters.Select((c, i) => $"""
                <navPoint id="ch{i + 1:D3}" playOrder="{i + 1}">
                  <navLabel><text>{Esc(c.Title)}</text></navLabel>
                  <content src="chapter-{i + 1:D3}.xhtml"/>
                </navPoint>
            """));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:uid" content="{bookId}"/>
                <meta name="dtb:depth" content="1"/>
                <meta name="dtb:totalPageCount" content="0"/>
                <meta name="dtb:maxPageNumber" content="0"/>
              </head>
              <docTitle><text>{Esc(ctx.Title)}</text></docTitle>
              <navMap>
            {items}
              </navMap>
            </ncx>
            """;
    }

    private static string BuildOpf(string bookId, ExportContext ctx, string coverExt, string coverMime)
    {
        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();

        manifestItems.AppendLine("    <item id=\"styles\" href=\"styles.css\" media-type=\"text/css\"/>");
        manifestItems.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        manifestItems.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
        manifestItems.AppendLine("    <item id=\"title\" href=\"title.xhtml\" media-type=\"application/xhtml+xml\"/>");
        manifestItems.AppendLine("    <item id=\"copyright\" href=\"copyright.xhtml\" media-type=\"application/xhtml+xml\"/>");

        if (ctx.Cover is not null)
        {
            manifestItems.AppendLine(
                $"    <item id=\"cover-image\" href=\"cover.{coverExt}\" media-type=\"{coverMime}\" properties=\"cover-image\"/>");
        }

        spineItems.AppendLine("    <itemref idref=\"title\"/>");
        spineItems.AppendLine("    <itemref idref=\"copyright\"/>");

        for (var i = 0; i < ctx.Chapters.Count; i++)
        {
            var id = $"ch{i + 1:D3}";
            manifestItems.AppendLine($"    <item id=\"{id}\" href=\"chapter-{i + 1:D3}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine($"    <itemref idref=\"{id}\"/>");
        }

        // EPUB 2 cover compatibility meta — supported by older readers.
        var coverMeta = ctx.Cover is null ? "" : "    <meta name=\"cover\" content=\"cover-image\"/>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid" xml:lang="en">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">{bookId}</dc:identifier>
                <dc:title>{Esc(ctx.Title)}</dc:title>
                <dc:language>en</dc:language>
                <dc:creator>{Esc(ctx.AuthorPenName ?? "Unknown")}</dc:creator>
                <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
            {coverMeta}
              </metadata>
              <manifest>
            {manifestItems}
              </manifest>
              <spine toc="ncx">
            {spineItems}
              </spine>
            </package>
            """;
    }

    // ----- helpers ----------------------------------------------------------

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Xhtml(string body, string title) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
        <head>
          <meta charset="UTF-8"/>
          <title>{Esc(title)}</title>
          <link rel="stylesheet" type="text/css" href="styles.css"/>
        </head>
        <body>
        {body}
        </body>
        </html>
        """;

    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&#39;");
    }
}
