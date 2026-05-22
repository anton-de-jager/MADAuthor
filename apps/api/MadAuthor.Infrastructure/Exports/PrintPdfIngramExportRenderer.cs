using MadAuthor.Application.Exports;
using MadAuthor.Infrastructure.Exports.Markdown;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MadAuthor.Infrastructure.Exports;

/// <summary>
/// Print-ready interior PDF for IngramSpark (6" × 9" trim).
/// Ingram-specific specs:
///   • Inside (gutter): 0.875" (perfect-binding tolerance)
///   • Outside: 0.5"
///   • Top/Bottom: 0.75"
///   • 0.125" bleed buffer on all sides — final page size 6.25" × 9.25",
///     content centred so the live area equals the 6" × 9" trim.
///   • PDF/A-style flatness (no transparency, no hyperlinks).
/// </summary>
public class PrintPdfIngramExportRenderer : IExportRenderer
{
    private const float Pt = 72f;
    private const float TrimW = 6f * Pt;
    private const float TrimH = 9f * Pt;
    private const float Bleed = 0.125f * Pt;     // 9pt
    private const float PageW = TrimW + 2 * Bleed; // 6.25"
    private const float PageH = TrimH + 2 * Bleed; // 9.25"

    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var bytes = Document.Create(container =>
        {
            // Title page (odd / right-hand)
            container.Page(p =>
            {
                ApplyPage(p);
                ApplyMirroredMargins(p, oddPage: true);
                p.DefaultTextStyle(BodyStyle);
                p.Content().Column(col =>
                {
                    col.Item().PaddingTop(120).AlignCenter().Text(ctx.Title)
                        .FontSize(26).Bold();
                    if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
                        col.Item().PaddingTop(12).AlignCenter().Text(ctx.Subtitle)
                            .FontSize(14).Italic().FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
                        col.Item().PaddingTop(40).AlignCenter().Text("by " + ctx.AuthorPenName).FontSize(12);
                });
            });

            // Copyright page (verso)
            container.Page(p =>
            {
                ApplyPage(p);
                ApplyMirroredMargins(p, oddPage: false);
                p.DefaultTextStyle(s => BodyStyle(s).FontSize(9));
                p.Content().AlignBottom().Column(col =>
                {
                    col.Spacing(6);
                    if (!string.IsNullOrWhiteSpace(ctx.CopyrightText))
                        col.Item().Text(ctx.CopyrightText);
                    col.Item().Text($"© {DateTime.UtcNow.Year} {ctx.AuthorPenName ?? "Author"}. All rights reserved.");
                    if (!string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText))
                    {
                        col.Item().PaddingTop(12).Text(ctx.Cover.AttributionText!)
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrWhiteSpace(ctx.Cover.AttributionUrl))
                            col.Item().Text(ctx.Cover.AttributionUrl!).FontSize(8).FontColor(Colors.Grey.Medium);
                    }
                    col.Item().Text("Printed by IngramSpark. Published with MADAuthor.")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });

            // ToC (odd)
            container.Page(p =>
            {
                ApplyPage(p);
                ApplyMirroredMargins(p, oddPage: true);
                p.DefaultTextStyle(BodyStyle);
                p.Header().Text("Contents").FontSize(20).Bold();
                p.Content().PaddingTop(18).Column(col =>
                {
                    col.Spacing(4);
                    foreach (var ch in ctx.Chapters)
                        col.Item().Text($"{ch.Number}.   {ch.Title}");
                });
            });

            var oddNext = true;
            foreach (var chapter in ctx.Chapters)
            {
                if (!oddNext)
                {
                    container.Page(p =>
                    {
                        ApplyPage(p);
                        ApplyMirroredMargins(p, oddPage: false);
                        p.Content().Text(" ");
                    });
                    oddNext = true;
                }

                container.Page(p =>
                {
                    ApplyPage(p);
                    ApplyMirroredMargins(p, oddPage: true);
                    p.DefaultTextStyle(BodyStyle);

                    p.Header().AlignCenter().Text(ctx.Title.ToUpperInvariant())
                        .FontSize(8).LetterSpacing(0.1f).FontColor(Colors.Grey.Darken1);

                    p.Content().PaddingTop(40).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text($"Chapter {chapter.Number}").FontSize(9)
                            .FontColor(Colors.Grey.Darken1).LetterSpacing(0.15f);
                        col.Item().PaddingBottom(18).Text(chapter.Title).FontSize(22).Bold();
                        foreach (var block in MarkdownFlattener.Flatten(chapter.ContentMarkdown))
                            RenderBlock(col, block);
                    });

                    p.Footer().AlignCenter().Text(t =>
                        t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1));
                });

                oddNext = !oddNext;
            }
        })
        .WithSettings(new DocumentSettings { PdfA = true })
        .GeneratePdf();

        var safe = SafeFile(ctx.Title);
        return Task.FromResult(new RenderedExport(bytes, $"{safe}-ingram-print.pdf", "application/pdf"));
    }

    private static void ApplyPage(PageDescriptor p)
    {
        // Full page size includes the 0.125" bleed on each side; the actual content
        // is constrained by the larger gutter margin so the live area lands inside trim.
        p.Size(PageW, PageH, Unit.Point);
        p.PageColor(Colors.White);
    }

    private static void ApplyMirroredMargins(PageDescriptor p, bool oddPage)
    {
        // Margins are measured from the bleed edge, so we add Bleed to each
        // edge to keep the live area equivalent to KDP-style margins inside trim.
        p.MarginTop(0.75f * Pt + Bleed, Unit.Point);
        p.MarginBottom(0.75f * Pt + Bleed, Unit.Point);
        if (oddPage)
        {
            p.MarginLeft(0.875f * Pt + Bleed, Unit.Point); // gutter
            p.MarginRight(0.5f * Pt + Bleed, Unit.Point);
        }
        else
        {
            p.MarginLeft(0.5f * Pt + Bleed, Unit.Point);
            p.MarginRight(0.875f * Pt + Bleed, Unit.Point); // gutter
        }
    }

    private static TextStyle BodyStyle(TextStyle s)
        => s.FontFamily("Georgia").FontSize(11).LineHeight(1.27f).FontColor(Colors.Black);

    private static void RenderBlock(ColumnDescriptor col, MarkdownBlock block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var size = h.Level switch { 1 => 18, 2 => 15, 3 => 13, _ => 12 };
                col.Item().PaddingTop(10).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(size).Bold());
                    if (h.Runs is { Count: > 0 }) RenderRuns(t, h.Runs);
                    else t.Span(h.Text);
                });
                break;
            }
            case ParagraphBlock p:
                col.Item().Text(t =>
                {
                    if (p.Runs is { Count: > 0 }) RenderRuns(t, p.Runs);
                    else t.Span(p.Text);
                });
                break;
            case BulletItemBlock b:
                col.Item().Row(r =>
                {
                    r.ConstantItem(12).Text("•");
                    r.RelativeItem().Text(t =>
                    {
                        if (b.Runs is { Count: > 0 }) RenderRuns(t, b.Runs);
                        else t.Span(b.Text);
                    });
                });
                break;
            case NumberedItemBlock n:
                col.Item().Row(r =>
                {
                    r.ConstantItem(20).Text($"{n.Index}.");
                    r.RelativeItem().Text(t =>
                    {
                        if (n.Runs is { Count: > 0 }) RenderRuns(t, n.Runs);
                        else t.Span(n.Text);
                    });
                });
                break;
            case QuoteBlock q:
                col.Item().BorderLeft(2).BorderColor(Colors.Grey.Medium).PaddingLeft(8)
                    .Text(t =>
                    {
                        t.DefaultTextStyle(s => s.Italic().FontColor(Colors.Grey.Darken2));
                        if (q.Runs is { Count: > 0 }) RenderRuns(t, q.Runs);
                        else t.Span(q.Text);
                    });
                break;
            case CodeBlock c:
                col.Item().Background(Colors.Grey.Lighten4).Padding(6)
                    .Text(c.Text).FontFamily("Consolas").FontSize(9);
                break;
            case ThematicBreakBlock:
                col.Item().PaddingVertical(8).AlignCenter().Text("* * *").FontColor(Colors.Grey.Darken1);
                break;
        }
    }

    private static void RenderRuns(QuestPDF.Fluent.TextDescriptor t,
        IReadOnlyList<InlineRun> runs, bool bold = false, bool italic = false)
    {
        foreach (var run in runs)
        {
            switch (run)
            {
                case TextRun tr:
                {
                    var span = t.Span(tr.Text);
                    if (bold) span.Bold();
                    if (italic) span.Italic();
                    break;
                }
                case BoldRun br: RenderRuns(t, br.Children, true, italic); break;
                case ItalicRun ir: RenderRuns(t, ir.Children, bold, true); break;
                case CodeRun cr:
                {
                    var span = t.Span(cr.Text).FontFamily("Consolas");
                    if (bold) span.Bold();
                    if (italic) span.Italic();
                    break;
                }
                case LinkRun lr:
                {
                    var span = t.Span(lr.Text);
                    if (bold) span.Bold();
                    if (italic) span.Italic();
                    break;
                }
                case SoftBreakRun:
                    t.Span(" ");
                    break;
            }
        }
    }

    private static string SafeFile(string title)
    {
        var s = new string((title ?? "book").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return s.Trim('-').ToLowerInvariant();
    }
}
