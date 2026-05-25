using MadAuthor.Application.Exports;
using MadAuthor.Infrastructure.Exports.Markdown;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MadAuthor.Infrastructure.Exports;

/// <summary>
/// Screen PDF (A5) — the everyday download. Same typographic chrome as the
/// print renderers (running header in small caps, drop-cap chapter openings,
/// ornaments for breaks) but tuned for screen reading at ~280 wpp. Live
/// hyperlinks are kept (no PDF/A requirement here).
/// </summary>
public class PdfExportRenderer : IExportRenderer
{
    private const string Ornament = "✦";
    private const string OrnamentBreak = "✦   ✦   ✦";

    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var bodyFont = string.IsNullOrWhiteSpace(ctx.BodyFont) ? "Georgia" : ctx.BodyFont!;

        TextStyle BodyStyle(TextStyle s)
            => s.FontFamily(bodyFont).FontSize(11).LineHeight(1.4f).FontColor(Colors.Black);

        var bytes = Document.Create(container =>
        {
            // ----- Title page ---------------------------------------------
            // Two layouts:
            //   A. Designed cover (typography pre-baked by the cover composer): render the
            //      image full-bleed across the entire page. No ornament/title text block - the
            //      cover already has all of that overlaid. Suppress the footer too so nothing
            //      sits over the artwork.
            //   B. Raw photo / no cover: keep the legacy small-photo + centered title layout
            //      so unstyled exports still look composed.
            container.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(0);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(BodyStyle);

                if (ctx.Cover is { IsDesigned: true } designed)
                {
                    p.Content().Image(designed.Bytes).FitArea();
                }
                else
                {
                    p.Content().Column(col =>
                    {
                        if (ctx.Cover is not null)
                        {
                            col.Item().Height(220, Unit.Point).Image(ctx.Cover.Bytes).FitArea();
                        }
                        col.Item().PaddingHorizontal(2, Unit.Centimetre).PaddingTop(ctx.Cover is null ? 100 : 30).Column(inner =>
                        {
                            inner.Item().AlignCenter().Text(Ornament).FontSize(18).FontColor(Colors.Grey.Darken1);
                            inner.Item().PaddingTop(22).AlignCenter().Text(ctx.Title).FontSize(28).Bold();
                            if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
                                inner.Item().PaddingTop(12).AlignCenter().Text(ctx.Subtitle)
                                    .FontSize(15).Italic().FontColor(Colors.Grey.Darken1);
                            if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
                                inner.Item().PaddingTop(48).AlignCenter().Text(t =>
                                {
                                    t.DefaultTextStyle(s => s.FontSize(11).LetterSpacing(0.15f).FontColor(Colors.Grey.Darken2));
                                    t.Span("by ").FontSize(10).FontColor(Colors.Grey.Darken1);
                                    t.Span(ctx.AuthorPenName!.ToUpperInvariant()).LetterSpacing(0.2f);
                                });
                        });
                    });

                    p.Footer().AlignCenter().PaddingBottom(8).Text("MADAuthor").FontSize(8).FontColor(Colors.Grey.Medium);
                }
            });

            // ----- Copyright page -----------------------------------------
            container.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(2, Unit.Centimetre);
                p.DefaultTextStyle(s => BodyStyle(s).FontSize(9));

                p.Content().AlignBottom().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"© {DateTime.UtcNow.Year} {ctx.AuthorPenName ?? "Author"}. All rights reserved.");
                    if (!string.IsNullOrWhiteSpace(ctx.CopyrightText))
                        col.Item().Text(ctx.CopyrightText);
                    if (!string.IsNullOrWhiteSpace(ctx.Cover?.AttributionText))
                    {
                        col.Item().PaddingTop(10).Text(ctx.Cover.AttributionText!)
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrWhiteSpace(ctx.Cover.AttributionUrl))
                            col.Item().Text(ctx.Cover.AttributionUrl!).FontSize(8).FontColor(Colors.Grey.Medium);
                    }
                    col.Item().PaddingTop(12).Text("Published with MADAuthor.")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });

            // ----- Table of contents ---------------------------------------
            container.Page(p =>
            {
                p.Size(PageSizes.A5);
                p.Margin(2, Unit.Centimetre);
                p.DefaultTextStyle(BodyStyle);

                p.Header().PaddingBottom(8).Column(c =>
                {
                    c.Item().AlignCenter().Text("Contents").FontSize(22).Bold();
                    c.Item().PaddingTop(6).AlignCenter().Text(Ornament).FontSize(12).FontColor(Colors.Grey.Darken1);
                });

                p.Content().PaddingTop(18).Column(col =>
                {
                    col.Spacing(9);
                    foreach (var ch in ctx.Chapters)
                    {
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(28).AlignRight().Text($"{ch.Number}.").FontSize(11).FontColor(Colors.Grey.Darken2);
                            r.ConstantItem(10);
                            r.RelativeItem().Text(ch.Title).FontSize(11);
                            r.AutoItem().PaddingLeft(8).Text(new string('.', 40)).FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    }
                });
            });

            // ----- Chapters -------------------------------------------------
            foreach (var chapter in ctx.Chapters)
            {
                container.Page(p =>
                {
                    p.Size(PageSizes.A5);
                    p.Margin(2, Unit.Centimetre);
                    p.DefaultTextStyle(BodyStyle);

                    // Small running header — book title in small caps. NO
                    // repeated chapter heading on every page.
                    p.Header().AlignCenter().Text(ctx.Title.ToUpperInvariant())
                        .FontSize(8).LetterSpacing(0.1f).FontColor(Colors.Grey.Darken1);

                    p.Content().Column(col =>
                    {
                        col.Item().PaddingTop(64).AlignCenter()
                            .Text($"CHAPTER {chapter.Number}")
                            .FontSize(9).LetterSpacing(0.15f).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(8).AlignCenter().Text(Ornament)
                            .FontSize(14).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(12).AlignCenter().Text(chapter.Title)
                            .FontSize(22).Bold();
                        col.Item().PaddingTop(8).PaddingBottom(18).AlignCenter()
                            .Width(80, Unit.Point).LineHorizontal(0.6f).LineColor(Colors.Grey.Lighten1);

                        var blocks = MarkdownFlattener.FlattenStrippingTitleH1(chapter.ContentMarkdown, chapter.Title);
                        RenderChapterBody(col, blocks, bodyFont);
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

    private static void RenderChapterBody(ColumnDescriptor col, IReadOnlyList<MarkdownBlock> blocks, string bodyFont)
    {
        var firstParagraphPending = true;
        var suppressNextSpacing = true;

        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock h:
                {
                    var (size, italic, top) = h.Level switch
                    {
                        1 => (16, false, 16),
                        2 => (14, false, 14),
                        3 => (12, true, 12),
                        _ => (11, true, 10)
                    };
                    col.Item().PaddingTop(top).ShowEntire().Text(t =>
                    {
                        t.DefaultTextStyle(s => italic
                            ? s.FontSize(size).Bold().Italic()
                            : s.FontSize(size).Bold());
                        if (h.Runs is { Count: > 0 }) RenderRuns(t, h.Runs);
                        else t.Span(h.Text);
                    });
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    break;
                }
                case ParagraphBlock pb:
                {
                    if (firstParagraphPending)
                    {
                        RenderDropCapParagraph(col, pb, bodyFont);
                        firstParagraphPending = false;
                    }
                    else
                    {
                        var container = col.Item();
                        if (!suppressNextSpacing) container = container.PaddingTop(6, Unit.Point);
                        container.Text(t =>
                        {
                            t.Justify();
                            if (pb.Runs is { Count: > 0 }) RenderRuns(t, pb.Runs);
                            else t.Span(pb.Text);
                        });
                    }
                    suppressNextSpacing = false;
                    break;
                }
                case BulletItemBlock b:
                    col.Item().ShowEntire().Row(r =>
                    {
                        r.ConstantItem(12).Text("•");
                        r.RelativeItem().Text(t =>
                        {
                            if (b.Runs is { Count: > 0 }) RenderRuns(t, b.Runs);
                            else t.Span(b.Text);
                        });
                    });
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    break;
                case NumberedItemBlock n:
                    col.Item().ShowEntire().Row(r =>
                    {
                        r.ConstantItem(20).Text($"{n.Index}.");
                        r.RelativeItem().Text(t =>
                        {
                            if (n.Runs is { Count: > 0 }) RenderRuns(t, n.Runs);
                            else t.Span(n.Text);
                        });
                    });
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    break;
                case QuoteBlock q:
                    col.Item().PaddingTop(6).PaddingBottom(6).BorderLeft(2)
                        .BorderColor(Colors.Grey.Medium).PaddingLeft(10)
                        .Text(t =>
                        {
                            t.Justify();
                            t.DefaultTextStyle(s => s.Italic().FontColor(Colors.Grey.Darken2));
                            t.Span("❧ ").FontSize(11).FontColor(Colors.Grey.Darken1);
                            if (q.Runs is { Count: > 0 }) RenderRuns(t, q.Runs);
                            else t.Span(q.Text);
                        });
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    break;
                case CodeBlock c:
                    col.Item().ShowEntire().Background(Colors.Grey.Lighten4).Padding(6)
                        .Text(c.Text).FontFamily("Consolas").FontSize(9);
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    break;
                case ThematicBreakBlock:
                    col.Item().PaddingVertical(12).AlignCenter().Text(OrnamentBreak)
                        .FontSize(14).FontColor(Colors.Grey.Darken1).LetterSpacing(0.3f);
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    break;
            }
        }
    }

    private static void RenderDropCapParagraph(ColumnDescriptor col, ParagraphBlock pb, string bodyFont)
    {
        var plain = pb.Runs is { Count: > 0 }
            ? MarkdownFlattener.RunsToPlainText(pb.Runs)
            : pb.Text ?? string.Empty;

        var firstLetterIndex = -1;
        for (int i = 0; i < plain.Length; i++)
        {
            if (char.IsLetter(plain[i])) { firstLetterIndex = i; break; }
        }

        if (firstLetterIndex < 0)
        {
            col.Item().Text(t =>
            {
                t.Justify();
                if (pb.Runs is { Count: > 0 }) RenderRuns(t, pb.Runs);
                else t.Span(plain);
            });
            return;
        }

        var dropChar = char.ToUpperInvariant(plain[firstLetterIndex]).ToString();
        var rest = plain.Substring(firstLetterIndex + 1);

        col.Item().Row(r =>
        {
            r.ConstantItem(30).PaddingTop(-2).Text(dropChar)
                .FontFamily(bodyFont).FontSize(34).Bold();
            r.RelativeItem().PaddingLeft(2).Text(t =>
            {
                t.Justify();
                t.Span(rest);
            });
        });
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
