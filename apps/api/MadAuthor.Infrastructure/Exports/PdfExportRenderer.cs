using MadAuthor.Application.Exports;
using MadAuthor.Infrastructure.Exports.Markdown;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MadAuthor.Infrastructure.Exports;

public class PdfExportRenderer : IExportRenderer
{
    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var bytes = Document.Create(container =>
        {
            // Title page — cover image (if any) above the text block
            container.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(0);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(t => t.FontFamily("Georgia").FontSize(12).LineHeight(1.4f));

                p.Content().Column(col =>
                {
                    if (ctx.Cover is not null)
                    {
                        col.Item().Height(220, Unit.Point).Image(ctx.Cover.Bytes).FitArea();
                    }
                    col.Item().PaddingHorizontal(2, Unit.Centimetre).PaddingTop(40).Column(inner =>
                    {
                        inner.Spacing(16);
                        inner.Item().AlignCenter().Text(ctx.Title).FontSize(26).Bold();
                        if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
                            inner.Item().AlignCenter().Text(ctx.Subtitle).FontSize(15).Italic().FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
                            inner.Item().AlignCenter().PaddingTop(14).Text("by " + ctx.AuthorPenName).FontSize(13);
                    });
                });

                p.Footer().AlignCenter().PaddingBottom(8).Text("MADAuthor").FontSize(9).FontColor(Colors.Grey.Medium);
            });

            // Copyright page — includes cover attribution per Unsplash license terms
            container.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(2, Unit.Centimetre);
                p.DefaultTextStyle(t => t.FontFamily("Georgia").FontSize(10).LineHeight(1.4f));

                p.Content().AlignBottom().Column(col =>
                {
                    col.Spacing(8);
                    if (!string.IsNullOrWhiteSpace(ctx.CopyrightText))
                        col.Item().Text(ctx.CopyrightText);
                    col.Item().Text($"© {DateTime.UtcNow.Year} {ctx.AuthorPenName ?? "Author"}. All rights reserved.");
                    if (!string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText))
                    {
                        col.Item().PaddingTop(12).Text(ctx.Cover.AttributionText!)
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrWhiteSpace(ctx.Cover.AttributionUrl))
                            col.Item().Text(ctx.Cover.AttributionUrl!).FontSize(8).FontColor(Colors.Grey.Medium);
                    }
                    col.Item().Text("Published with MADAuthor.").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });

            // Table of contents
            container.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(2, Unit.Centimetre);
                p.DefaultTextStyle(t => t.FontFamily("Georgia").FontSize(11).LineHeight(1.6f));
                p.Header().Text("Contents").FontSize(22).Bold();
                p.Content().PaddingTop(15).Column(col =>
                {
                    foreach (var ch in ctx.Chapters)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"{ch.Number}.   {ch.Title}");
                        });
                    }
                });
            });

            // Each chapter on its own page-group
            foreach (var chapter in ctx.Chapters)
            {
                container.Page(p =>
                {
                    p.Size(PageSizes.A5);
                    p.Margin(2, Unit.Centimetre);
                    p.DefaultTextStyle(t => t.FontFamily("Georgia").FontSize(11).LineHeight(1.6f));

                    p.Header().Column(c =>
                    {
                        c.Item().Text($"Chapter {chapter.Number}").FontSize(9)
                            .FontColor(Colors.Grey.Medium).LetterSpacing(0.1f);
                        c.Item().Text(chapter.Title).FontSize(22).Bold();
                        c.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                    });

                    p.Content().PaddingTop(12).Column(col =>
                    {
                        col.Spacing(8);
                        foreach (var block in MarkdownFlattener.Flatten(chapter.ContentMarkdown))
                        {
                            RenderBlock(col, block);
                        }
                    });

                    p.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                });
            }
        })
        .GeneratePdf();

        var safe = SafeFile(ctx.Title);
        return Task.FromResult(new RenderedExport(bytes, $"{safe}.pdf", "application/pdf"));
    }

    private static void RenderBlock(ColumnDescriptor col, MarkdownBlock block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var size = h.Level switch { 1 => 20, 2 => 16, 3 => 14, _ => 12 };
                col.Item().PaddingTop(10).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(size).Bold());
                    if (h.Runs is { Count: > 0 })
                        RenderRuns(t, h.Runs);
                    else
                        t.Span(h.Text);
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
                col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                break;
        }
    }

    /// <summary>Renders an inline-run tree into a QuestPDF TextDescriptor,
    /// applying Bold/Italic/Code/Link spans to preserve markdown emphasis.</summary>
    private static void RenderRuns(QuestPDF.Fluent.TextDescriptor t,
        IReadOnlyList<InlineRun> runs,
        bool bold = false, bool italic = false)
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
                case BoldRun br:
                    RenderRuns(t, br.Children, bold: true, italic: italic);
                    break;
                case ItalicRun ir:
                    RenderRuns(t, ir.Children, bold: bold, italic: true);
                    break;
                case CodeRun cr:
                {
                    var span = t.Span(cr.Text).FontFamily("Consolas").BackgroundColor(Colors.Grey.Lighten4);
                    if (bold) span.Bold();
                    if (italic) span.Italic();
                    break;
                }
                case LinkRun lr:
                {
                    var span = string.IsNullOrEmpty(lr.Url)
                        ? t.Span(lr.Text)
                        : t.Hyperlink(lr.Text, lr.Url!);
                    span.FontColor(Colors.Blue.Medium).Underline();
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
