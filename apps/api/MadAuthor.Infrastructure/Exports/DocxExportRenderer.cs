using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MadAuthor.Application.Exports;
using MadAuthor.Infrastructure.Exports.Markdown;

namespace MadAuthor.Infrastructure.Exports;

public class DocxExportRenderer : IExportRenderer
{
    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            // Title page
            AppendHeading(body, ctx.Title, level: 1, fontSize: 48, alignment: JustificationValues.Center);
            if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
                AppendParagraph(body, ctx.Subtitle!, italic: true, alignment: JustificationValues.Center);
            if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
                AppendParagraph(body, "by " + ctx.AuthorPenName, alignment: JustificationValues.Center);
            AppendPageBreak(body);

            // Copyright page
            if (!string.IsNullOrWhiteSpace(ctx.CopyrightText))
                AppendParagraph(body, ctx.CopyrightText!);
            AppendParagraph(body,
                $"© {DateTime.UtcNow.Year} {ctx.AuthorPenName ?? "Author"}. All rights reserved.");
            AppendParagraph(body, "Published with MADAuthor.");
            AppendPageBreak(body);

            // TOC
            AppendHeading(body, "Contents", level: 1, fontSize: 32);
            foreach (var ch in ctx.Chapters)
                AppendParagraph(body, $"{ch.Number}.  {ch.Title}");
            AppendPageBreak(body);

            // Chapters
            foreach (var chapter in ctx.Chapters)
            {
                AppendParagraph(body, $"Chapter {chapter.Number}", italic: true);
                AppendHeading(body, chapter.Title, level: 1, fontSize: 32);

                foreach (var block in MarkdownFlattener.Flatten(chapter.ContentMarkdown))
                {
                    switch (block)
                    {
                        case HeadingBlock h:
                            AppendHeading(body, h.Text, h.Level,
                                fontSize: h.Level switch { 1 => 28, 2 => 22, 3 => 18, _ => 14 },
                                runs: h.Runs);
                            break;
                        case ParagraphBlock p:
                            AppendParagraph(body, p.Text, runs: p.Runs);
                            break;
                        case BulletItemBlock b:
                            AppendListItem(body, prefix: "• ", text: b.Text, runs: b.Runs);
                            break;
                        case NumberedItemBlock n:
                            AppendListItem(body, prefix: $"{n.Index}. ", text: n.Text, runs: n.Runs);
                            break;
                        case QuoteBlock q:
                            AppendParagraph(body, q.Text, italic: true, runs: q.Runs);
                            break;
                        case CodeBlock c:
                            AppendParagraph(body, c.Text, monospace: true);
                            break;
                        case ThematicBreakBlock:
                            AppendParagraph(body, "* * *", alignment: JustificationValues.Center);
                            break;
                    }
                }
                AppendPageBreak(body);
            }

            main.Document.Save();
        }

        var bytes = ms.ToArray();
        var safe = new string((ctx.Title ?? "book").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-').ToLowerInvariant();
        return Task.FromResult(new RenderedExport(
            bytes, $"{safe}.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
    }

    private static void AppendHeading(Body body, string text, int level, int fontSize,
        JustificationValues? alignment = null, IReadOnlyList<InlineRun>? runs = null)
    {
        var pProps = new ParagraphProperties(
            new ParagraphStyleId { Val = $"Heading{Math.Clamp(level, 1, 6)}" },
            new SpacingBetweenLines { Before = "240", After = "120" });
        if (alignment is not null)
            pProps.AppendChild(new Justification { Val = alignment.Value });

        var paragraph = new Paragraph(pProps);

        if (runs is { Count: > 0 })
        {
            foreach (var run in BuildRuns(runs, baseBold: true, baseItalic: false, fontSize: fontSize))
                paragraph.AppendChild(run);
        }
        else
        {
            paragraph.AppendChild(new Run(
                new RunProperties(new Bold(), new FontSize { Val = (fontSize * 2).ToString() }),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        body.AppendChild(paragraph);
    }

    private static void AppendParagraph(Body body, string text,
        bool italic = false, bool monospace = false, JustificationValues? alignment = null,
        IReadOnlyList<InlineRun>? runs = null)
    {
        var pProps = new ParagraphProperties(
            new SpacingBetweenLines { After = "120", Line = "360" });
        if (alignment is not null)
            pProps.AppendChild(new Justification { Val = alignment.Value });

        var paragraph = new Paragraph(pProps);

        if (runs is { Count: > 0 })
        {
            foreach (var run in BuildRuns(runs, baseBold: false, baseItalic: italic))
                paragraph.AppendChild(run);
        }
        else
        {
            var props = new RunProperties();
            if (italic) props.AppendChild(new Italic());
            if (monospace) props.AppendChild(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            paragraph.AppendChild(new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        body.AppendChild(paragraph);
    }

    private static void AppendListItem(Body body, string prefix, string text, IReadOnlyList<InlineRun>? runs)
    {
        var pProps = new ParagraphProperties(new SpacingBetweenLines { After = "120", Line = "360" });
        var paragraph = new Paragraph(pProps);

        paragraph.AppendChild(new Run(new RunProperties(),
            new Text(prefix) { Space = SpaceProcessingModeValues.Preserve }));

        if (runs is { Count: > 0 })
        {
            foreach (var run in BuildRuns(runs, baseBold: false, baseItalic: false))
                paragraph.AppendChild(run);
        }
        else
        {
            paragraph.AppendChild(new Run(new RunProperties(),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        body.AppendChild(paragraph);
    }

    /// <summary>Walks an inline-run tree and emits OpenXML <c>Run</c> elements with Bold/Italic
    /// applied per-span. Recursively combines nested emphasis (bold-italic, etc.).</summary>
    private static IEnumerable<Run> BuildRuns(IReadOnlyList<InlineRun> runs,
        bool baseBold, bool baseItalic, int? fontSize = null)
    {
        var emitted = new List<Run>();
        foreach (var r in runs) EmitRun(r, baseBold, baseItalic, fontSize, emitted);
        return emitted;
    }

    private static void EmitRun(InlineRun run, bool bold, bool italic, int? fontSize, List<Run> output)
    {
        switch (run)
        {
            case TextRun t:
                output.Add(BuildSingleRun(t.Text, bold, italic, monospace: false, fontSize: fontSize));
                break;
            case BoldRun b:
                foreach (var c in b.Children) EmitRun(c, true, italic, fontSize, output);
                break;
            case ItalicRun i:
                foreach (var c in i.Children) EmitRun(c, bold, true, fontSize, output);
                break;
            case CodeRun c:
                output.Add(BuildSingleRun(c.Text, bold, italic, monospace: true, fontSize: fontSize));
                break;
            case LinkRun l:
                // OpenXML hyperlinks require relationship parts; render link text as a styled run instead.
                output.Add(BuildSingleRun(l.Text, bold, italic, monospace: false, fontSize: fontSize, link: true));
                break;
            case SoftBreakRun:
                output.Add(BuildSingleRun(" ", bold, italic, monospace: false, fontSize: fontSize));
                break;
        }
    }

    private static Run BuildSingleRun(string text, bool bold, bool italic, bool monospace, int? fontSize, bool link = false)
    {
        var props = new RunProperties();
        if (bold) props.AppendChild(new Bold());
        if (italic) props.AppendChild(new Italic());
        if (monospace) props.AppendChild(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
        if (link) props.AppendChild(new Underline { Val = UnderlineValues.Single });
        if (fontSize is not null) props.AppendChild(new FontSize { Val = (fontSize.Value * 2).ToString() });
        return new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static void AppendPageBreak(Body body)
    {
        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
    }
}
