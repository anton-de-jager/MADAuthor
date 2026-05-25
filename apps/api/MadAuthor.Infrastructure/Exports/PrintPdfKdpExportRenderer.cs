using MadAuthor.Application.Exports;
using MadAuthor.Infrastructure.Exports.Markdown;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MadAuthor.Infrastructure.Exports;

/// <summary>
/// Print-ready interior PDF sized for Amazon KDP Paperback (6" × 9" trim).
/// Trade-paperback typographic targets:
///   • Body 11pt Georgia, line-height 1.35 (≈14.85pt leading) → ~290 wpp.
///   • Inside (gutter): 0.75"  • Outside: 0.625"  • Top/Bottom: 0.75".
///   • Mirrored margins for facing pages.
///   • Running header is the book title in 8pt small-caps style (uppercase
///     letters with letter-spacing). No per-page chapter heading repetition.
///   • Chapter opening: 1.5" top whitespace, small-caps "CHAPTER N", centred
///     ornament, large title with a thin centred rule, drop-cap first letter.
///   • Standard book paragraph rhythm: 0.25" first-line indent except the
///     first paragraph after a heading or thematic break.
/// No bleed (KDP interior pages are non-bleed). Cover-wrap is uploaded
/// separately via KDP; this file is the interior only.
/// </summary>
public class PrintPdfKdpExportRenderer : IExportRenderer
{
    private const float Pt = 72f;
    private const float TrimW = 6f * Pt;
    private const float TrimH = 9f * Pt;
    private const string Ornament = "✦";           // ✦
    private const string OrnamentBreak = "✦   ✦   ✦";

    public Task<RenderedExport> RenderAsync(ExportContext ctx, CancellationToken ct = default)
    {
        var bodyFont = string.IsNullOrWhiteSpace(ctx.BodyFont) ? "Georgia" : ctx.BodyFont!;

        TextStyle BodyStyle(TextStyle s)
            => s.FontFamily(bodyFont).FontSize(11).LineHeight(1.35f).FontColor(Colors.Black);

        var bytes = Document.Create(container =>
        {
            // ----- Title page (right-hand, page 1) -------------------------
            // When a designed cover exists, render it full-bleed and skip the text title
            // block - the composer has already overlaid title/subtitle/author. Otherwise
            // fall back to the small-image + centered text layout.
            container.Page(p =>
            {
                ApplyTrim(p);
                if (ctx.Cover is not { IsDesigned: true })
                    ApplyMirroredMargins(p, oddPage: true);
                else
                    p.Margin(0);
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
                            col.Item().PaddingTop(36).AlignCenter()
                                .MaxHeight(3f * Pt, Unit.Point)
                                .Image(ctx.Cover.Bytes).FitArea();
                        }

                        col.Item().PaddingTop(ctx.Cover is null ? 180 : 60).AlignCenter()
                            .Text(Ornament).FontSize(18).FontColor(Colors.Grey.Darken1);

                        col.Item().PaddingTop(28).AlignCenter().Text(ctx.Title)
                            .FontSize(30).Bold();

                        if (!string.IsNullOrWhiteSpace(ctx.Subtitle))
                            col.Item().PaddingTop(14).AlignCenter().Text(ctx.Subtitle)
                                .FontSize(15).Italic().FontColor(Colors.Grey.Darken1);

                        if (!string.IsNullOrWhiteSpace(ctx.AuthorPenName))
                            col.Item().PaddingTop(80).AlignCenter().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(11).LetterSpacing(0.15f).FontColor(Colors.Grey.Darken2));
                                t.Span("by ").FontSize(10).FontColor(Colors.Grey.Darken1);
                                t.Span(ctx.AuthorPenName!.ToUpperInvariant()).LetterSpacing(0.2f);
                            });
                    });
                }
            });

            // ----- Copyright page (left-hand) ------------------------------
            container.Page(p =>
            {
                ApplyTrim(p);
                ApplyMirroredMargins(p, oddPage: false);
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
                    col.Item().PaddingTop(14).Text("Printed by Amazon KDP. Published with MADAuthor.")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });

            // ----- Table of contents (right-hand) --------------------------
            container.Page(p =>
            {
                ApplyTrim(p);
                ApplyMirroredMargins(p, oddPage: true);
                p.DefaultTextStyle(BodyStyle);

                p.Header().PaddingBottom(8).Column(c =>
                {
                    c.Item().AlignCenter().Text("Contents").FontSize(22).Bold();
                    c.Item().PaddingTop(6).AlignCenter().Text(Ornament).FontSize(12).FontColor(Colors.Grey.Darken1);
                });

                p.Content().PaddingTop(20).Column(col =>
                {
                    col.Spacing(10);
                    foreach (var ch in ctx.Chapters)
                    {
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(28).AlignRight().Text($"{ch.Number}.").FontSize(11).FontColor(Colors.Grey.Darken2);
                            r.ConstantItem(10);
                            r.RelativeItem().Text(ch.Title).FontSize(11);
                            r.AutoItem().PaddingLeft(8).Text(BuildDottedLeader()).FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    }
                });
            });

            // ----- Chapters: each starts on an odd (right) page ------------
            var oddNext = true;
            foreach (var chapter in ctx.Chapters)
            {
                // Insert a blank verso page if the chapter would otherwise start
                // on an even page. QuestPDF doesn't expose ordinal page numbers,
                // so we track the expected parity ourselves.
                if (!oddNext)
                {
                    container.Page(p =>
                    {
                        ApplyTrim(p);
                        ApplyMirroredMargins(p, oddPage: false);
                        p.Content().Text(" ");
                    });
                    oddNext = true;
                }

                container.Page(p =>
                {
                    ApplyTrim(p);
                    ApplyMirroredMargins(p, oddPage: true);
                    p.DefaultTextStyle(BodyStyle);

                    // Running header: the book title in small-caps style.
                    // No chapter title here — it lives once in the opening.
                    p.Header().AlignCenter().Text(ctx.Title.ToUpperInvariant())
                        .FontSize(8).LetterSpacing(0.1f).FontColor(Colors.Grey.Darken1);

                    p.Content().Column(col =>
                    {
                        // 1.5" of top whitespace before the chapter-number line.
                        col.Item().PaddingTop(1.5f * Pt, Unit.Point).AlignCenter()
                            .Text($"CHAPTER {chapter.Number}")
                            .FontSize(9).LetterSpacing(0.15f).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(10).AlignCenter().Text(Ornament)
                            .FontSize(14).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(14).AlignCenter().Text(chapter.Title)
                            .FontSize(24).Bold();
                        col.Item().PaddingTop(8).PaddingBottom(22).AlignCenter()
                            .Width(1.5f * Pt, Unit.Point).LineHorizontal(0.6f).LineColor(Colors.Grey.Lighten1);

                        var blocks = MarkdownFlattener.FlattenStrippingTitleH1(chapter.ContentMarkdown, chapter.Title);
                        RenderChapterBody(col, blocks, bodyFont);
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
        return Task.FromResult(new RenderedExport(bytes, $"{safe}-kdp-print.pdf", "application/pdf"));
    }

    private static void ApplyTrim(PageDescriptor p)
    {
        p.Size(TrimW, TrimH, Unit.Point);
        p.PageColor(Colors.White);
    }

    private static void ApplyMirroredMargins(PageDescriptor p, bool oddPage)
    {
        // KDP recommends mirrored gutter margins so the inside edge has more
        // space. Outside widened to 0.625" to hit the 290 wpp target.
        p.MarginTop(0.75f, Unit.Inch);
        p.MarginBottom(0.75f, Unit.Inch);
        if (oddPage)
        {
            p.MarginLeft(0.75f, Unit.Inch);    // gutter (inside)
            p.MarginRight(0.625f, Unit.Inch);  // outside
        }
        else
        {
            p.MarginLeft(0.625f, Unit.Inch);   // outside
            p.MarginRight(0.75f, Unit.Inch);   // gutter (inside)
        }
    }

    private static string BuildDottedLeader()
        => new string('.', 48);

    /// <summary>Walks the flattened markdown for a chapter, rendering block-style
    /// paragraphs (no indent, small vertical spacing) and the drop-cap on the very first paragraph.</summary>
    private static void RenderChapterBody(ColumnDescriptor col, IReadOnlyList<MarkdownBlock> blocks, string bodyFont)
    {
        var firstParagraphPending = true;       // drop-cap candidate
        var suppressNextSpacing = true;         // first paragraph after the chapter heading skips the top padding
        var lastWasHeading = false;
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            switch (block)
            {
                case HeadingBlock h:
                {
                    // H1 is treated as a section break inside the chapter — we
                    // already drew the chapter title, so promote H1 to H2 styling.
                    var (size, italic, top) = h.Level switch
                    {
                        1 => (16, false, 18),
                        2 => (14, false, 16),
                        3 => (12, true, 14),
                        _ => (11, true, 12)
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
                    firstParagraphPending = false; // any heading kills the drop-cap candidacy
                    lastWasHeading = true;
                    break;
                }
                case ParagraphBlock pb:
                {
                    if (firstParagraphPending)
                    {
                        // Drop-cap rendering. Emulated via Row: first letter in a
                        // ConstantItem at a large display size, rest in a RelativeItem.
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
                    lastWasHeading = false;
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
                    lastWasHeading = false;
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
                    lastWasHeading = false;
                    break;
                case QuoteBlock q:
                    col.Item().PaddingTop(6).PaddingBottom(6).BorderLeft(2)
                        .BorderColor(Colors.Grey.Medium).PaddingLeft(10)
                        .Text(t =>
                        {
                            t.Justify();
                            t.DefaultTextStyle(s => s.Italic().FontColor(Colors.Grey.Darken2));
                            t.Span("❧ ").FontSize(11).FontColor(Colors.Grey.Darken1); // ❧
                            if (q.Runs is { Count: > 0 }) RenderRuns(t, q.Runs);
                            else t.Span(q.Text);
                        });
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    lastWasHeading = false;
                    break;
                case CodeBlock c:
                    col.Item().ShowEntire().Background(Colors.Grey.Lighten4).Padding(6)
                        .Text(c.Text).FontFamily("Consolas").FontSize(9);
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    lastWasHeading = false;
                    break;
                case ThematicBreakBlock:
                    col.Item().PaddingVertical(14).AlignCenter().Text(OrnamentBreak)
                        .FontSize(15).FontColor(Colors.Grey.Darken1).LetterSpacing(0.3f);
                    suppressNextSpacing = true;
                    firstParagraphPending = false;
                    lastWasHeading = false;
                    break;
            }
        }
        _ = lastWasHeading; // reserved for future use
    }

    private static void RenderDropCapParagraph(ColumnDescriptor col, ParagraphBlock pb, string bodyFont)
    {
        var plain = pb.Runs is { Count: > 0 }
            ? MarkdownFlattener.RunsToPlainText(pb.Runs)
            : pb.Text ?? string.Empty;

        // Find the first letter to use as a drop cap. Fall back to a plain
        // paragraph if the text doesn't start with a letter.
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
            r.ConstantItem(34).PaddingTop(-2).Text(dropChar)
                .FontFamily(bodyFont).FontSize(40).Bold();
            r.RelativeItem().PaddingLeft(2).Text(t =>
            {
                // We deliberately lose run-level formatting on the first
                // paragraph because slicing the inline tree at an arbitrary
                // character is more complexity than this typographic touch
                // deserves. The body picks formatting back up on paragraph 2.
                t.Justify();
                t.Span(rest);
            });
        });
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
                    // Print-ready: no live hyperlinks. Render link text plain so the
                    // PDF stays valid for PDF/A submission to KDP.
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
