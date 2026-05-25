using MadAuthor.Application.Covers;
using QuestPDF.Drawing;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MadAuthor.Infrastructure.Covers;

/// <summary>
/// QuestPDF-based cover composer. Renders each of the six templates as a 6x9 inch
/// panel (front or back) and stitches front + spine + back into a print-ready wrap PDF.
///
/// Why QuestPDF and not ImageSharp/SkiaSharp directly? QuestPDF already ships in this
/// solution for the chapter PDF exports, supports embedded images via <c>Image()</c>,
/// can rasterize a page to PNG via <c>.GenerateImages()</c>, and gives us layered/
/// flow layouts (Layers + Stack + Background) that match the template specs without
/// having to hand-roll Skia draw calls.
///
/// Resolution: <c>DocumentSettings.ImageRasterDpi</c> (300 for print PNG, 100 for the
/// live preview) controls the rasterization DPI. A 6x9 page at 300 DPI is 1800x2700 px.
/// </summary>
public class QuestPdfCoverComposer : ICoverComposer
{
    // 6x9 inches in PDF points (1 inch = 72 pt) → 432 x 648 pt panel.
    private const float PageWidthPt = 432f;
    private const float PageHeightPt = 648f;

    public Task<byte[]> ComposePanelAsync(CoverComposeRequest req, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult(RenderPanelToPng(req));
        }
        catch (DocumentLayoutException)
        {
            // The chosen template overflowed - typically the back panel with very long
            // synopsis/bio text. Re-render with a minimal-fallback back layout that uses
            // smaller fonts and aggressive truncation. We don't bubble the original
            // exception because it would surface the unhelpful "Could not compose back
            // of cover" toast to the user; a slightly less pretty back is strictly better
            // than a hard failure.
            return Task.FromResult(RenderPanelToPng(req, useFallbackBack: true));
        }
    }

    private static byte[] RenderPanelToPng(CoverComposeRequest req, bool useFallbackBack = false)
    {
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                p.Size(PageWidthPt, PageHeightPt, Unit.Point);
                p.Margin(0);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(s => s.FontColor(Colors.White));

                p.Content().Element(c =>
                {
                    if (useFallbackBack && req.Side == CoverSide.Back)
                        RenderFallbackBack(c, req);
                    else
                        RenderPanel(c, req);
                });
            });
        })
        .WithSettings(new DocumentSettings
        {
            // 300 DPI = print quality. 6x9 at 300 DPI = 1800 x 2700 px.
            ImageRasterDpi = 300,
            ImageCompressionQuality = ImageCompressionQuality.High,
            PdfA = false,
        });

        var images = doc.GenerateImages().ToList();
        // GenerateImages() returns one PNG per page; we have a single page per panel.
        return images.Count > 0 ? images[0] : Array.Empty<byte>();
    }

    /// <summary>
    /// Last-resort back-panel renderer. Pure typography on a solid background, with
    /// hard-truncated text and zero risk of overflow. Used when the template-specific
    /// back layout throws <see cref="DocumentLayoutException"/>.
    /// </summary>
    private static void RenderFallbackBack(IContainer c, CoverComposeRequest r)
    {
        c.Background("#0F172A").Padding(28, Unit.Point).Column(col =>
        {
            col.Item().Text(r.Title ?? string.Empty)
                .FontFamily("Georgia").FontSize(20).Bold().FontColor(Colors.White);
            if (!string.IsNullOrWhiteSpace(r.Subtitle))
            {
                col.Item().PaddingTop(2).Text(r.Subtitle!)
                    .FontFamily("Georgia").FontSize(12).Italic().FontColor(Colors.Grey.Lighten2);
            }
            col.Item().PaddingTop(10).LineHorizontal(0.6f).LineColor(Colors.Grey.Lighten3);

            col.Item().PaddingTop(12)
                .Text(TruncateForBack(r.Synopsis, 320)
                      ?? "[Back-cover synopsis - fill in via the Publishing tab.]")
                .FontFamily("Georgia").FontSize(10).LineHeight(1.35f).FontColor(Colors.White);

            col.Item().PaddingTop(12).Text("ABOUT THE AUTHOR")
                .FontFamily("Helvetica").FontSize(9).LetterSpacing(0.3f).Bold().FontColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(2)
                .Text(TruncateForBack(r.AuthorBio, 180)
                      ?? $"{r.AuthorPenName} writes about the things that matter.")
                .FontFamily("Georgia").FontSize(9).LineHeight(1.35f).FontColor(Colors.White);

            col.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(
                    string.IsNullOrWhiteSpace(r.ImprintName)
                        ? "Published with MADAuthor"
                        : $"Published with {r.ImprintName}")
                    .FontFamily("Georgia").FontSize(8.5f).Italic().FontColor(Colors.Grey.Lighten2);
                row.ConstantItem(IsbnBoxWidthPt).AlignRight()
                    .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                    .Background(Colors.White).AlignCenter().AlignMiddle()
                    .Text("ISBN").FontFamily("Helvetica").FontSize(10)
                    .LetterSpacing(0.3f).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    public Task<byte[]> RenderWrapAsync(CoverWrapRequest req, CancellationToken ct = default)
    {
        // Wrap layout: [bleed] back-panel [spine] front-panel [bleed].
        // Heights match the panel + bleed.
        const double bleedIn = 0.125;
        var bleed = req.IngramBleed ? bleedIn : 0.0;
        var totalWidthIn = 6.0 + 6.0 + req.SpineWidthInches + (2 * bleed);
        var totalHeightIn = 9.0 + (2 * bleed);

        var totalWidthPt = (float)(totalWidthIn * 72.0);
        var totalHeightPt = (float)(totalHeightIn * 72.0);
        var spineWidthPt = (float)(req.SpineWidthInches * 72.0);
        var panelWidthPt = (float)(6.0 * 72.0);
        var panelHeightPt = (float)(9.0 * 72.0);
        var bleedPt = (float)(bleed * 72.0);

        // Spine font size: scale to fit the spine width. Roughly 60% of the spine width
        // in points, capped between 8 and 22 pt so a 0.05" spine doesn't blow up.
        var spineFontPt = Math.Clamp(spineWidthPt * 0.6f, 8f, 22f);

        var bytes = Document.Create(container =>
        {
            container.Page(p =>
            {
                p.Size(totalWidthPt, totalHeightPt, Unit.Point);
                p.Margin(0);
                p.PageColor(Colors.White);

                p.Content().Row(row =>
                {
                    if (bleedPt > 0) row.ConstantItem(bleedPt).Background(Colors.Grey.Lighten5);

                    // Back panel (image flows left → right of the page when book is closed)
                    row.ConstantItem(panelWidthPt).Element(c =>
                    {
                        c.Height(panelHeightPt + (bleedPt * 2), Unit.Point)
                         .AlignMiddle()
                         .Image(req.BackPanelPng).FitArea();
                    });

                    // Spine: solid charcoal with title + author reading bottom→top.
                    // RotateLeft() rotates content 90° counter-clockwise and swaps the
                    // available width/height for the inner content, so the inner Text
                    // can be panelHeight-wide even though the outer column is only
                    // spineWidth-wide. ScaleToFit shrinks the text if it would overflow.
                    row.ConstantItem(spineWidthPt).Element(c =>
                    {
                        var title = req.SpineTitle?.Trim() ?? string.Empty;
                        var author = (req.SpineAuthor?.Trim() ?? string.Empty).ToUpperInvariant();
                        var authorFontPt = Math.Max(7f, spineFontPt * 0.6f);

                        c.Background("#1F1F1F")
                         .AlignCenter()
                         .AlignMiddle()
                         .RotateLeft()
                         .Width(panelHeightPt * 0.9f)
                         .ScaleToFit()
                         .Text(text =>
                         {
                             text.AlignCenter();
                             text.Span(title)
                                 .FontColor(Colors.White)
                                 .FontSize(spineFontPt)
                                 .Bold()
                                 .FontFamily("Arial");
                             if (!string.IsNullOrEmpty(author))
                             {
                                 text.Span("    ");
                                 text.Span(author)
                                     .FontColor("#BBBBBB")
                                     .FontSize(authorFontPt)
                                     .FontFamily("Arial");
                             }
                         });
                    });

                    // Front panel.
                    row.ConstantItem(panelWidthPt).Element(c =>
                    {
                        c.Height(panelHeightPt + (bleedPt * 2), Unit.Point)
                         .AlignMiddle()
                         .Image(req.FrontPanelPng).FitArea();
                    });

                    if (bleedPt > 0) row.ConstantItem(bleedPt).Background(Colors.Grey.Lighten5);
                });
            });
        })
        // KDP accepts standard PDF; PdfA tightens it but rejects some features. Off for the
        // wrap since the panels are pre-rasterized PNGs (no live text/hyperlinks anyway).
        .WithSettings(new DocumentSettings
        {
            ImageRasterDpi = 300,
            ImageCompressionQuality = ImageCompressionQuality.High,
            PdfA = false,
        })
        .GeneratePdf();

        return Task.FromResult(bytes);
    }

    // -- Template dispatch ----------------------------------------------------

    private static void RenderPanel(IContainer c, CoverComposeRequest r)
    {
        switch (r.Template)
        {
            case CoverTemplate.BoldGradient:     RenderBoldGradient(c, r); break;
            case CoverTemplate.ClassicCentered:  RenderClassicCentered(c, r); break;
            case CoverTemplate.ModernMinimal:    RenderModernMinimal(c, r); break;
            case CoverTemplate.PenguinStripe:    RenderPenguinStripe(c, r); break;
            case CoverTemplate.MagazineBlock:    RenderMagazineBlock(c, r); break;
            case CoverTemplate.AuthorSpotlight:  RenderAuthorSpotlight(c, r); break;
            case CoverTemplate.NightOwl:         RenderNightOwl(c, r); break;
            case CoverTemplate.GoldenAge:        RenderGoldenAge(c, r); break;
            default:                             RenderBoldGradient(c, r); break;
        }
    }

    // -- TEMPLATE 1: Bold Gradient -------------------------------------------
    private static void RenderBoldGradient(IContainer c, CoverComposeRequest r)
    {
        if (r.Side == CoverSide.Front)
        {
            c.Layers(layers =>
            {
                // Full-bleed background image.
                layers.Layer().Image(r.BackgroundImage).FitArea();

                // Dark→transparent gradient on the bottom 45% (approximated as two translucent
                // dark bands stacked – QuestPDF doesn't have a true gradient primitive, so we
                // use ARGB hex codes for the alpha. A real linear-gradient would require Skia
                // draw calls outside QuestPDF, which is out of scope.
                layers.Layer().Column(col =>
                {
                    col.Item().Height(PageHeightPt * 0.55f);
                    col.Item().Height(PageHeightPt * 0.18f).Background("#59000000"); // 35% alpha black
                    col.Item().Height(PageHeightPt * 0.27f).Background("#B3000000"); // 70% alpha black
                });

                // Title block — bottom-left, 32pt margin.
                layers.PrimaryLayer().PaddingHorizontal(32, Unit.Point).PaddingBottom(40, Unit.Point)
                    .AlignBottom().Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        col.Item().Text(r.Subtitle).FontFamily("Helvetica")
                            .FontSize(22).Italic().FontColor(Colors.Grey.Lighten2);
                        col.Item().PaddingBottom(6);
                    }
                    col.Item().Text((r.Title ?? string.Empty).ToLowerInvariant())
                        .FontFamily("Helvetica").FontSize(54).Bold().FontColor(Colors.White);
                    col.Item().PaddingTop(20).Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Helvetica").FontSize(14).LetterSpacing(0.25f).FontColor(Colors.White);
                });
            });
        }
        else
        {
            // Back: same gradient language, but with synopsis/bio/ISBN box.
            c.Layers(layers =>
            {
                layers.Layer().Image(r.BackgroundImage).FitArea();
                layers.Layer().Background("#99000000"); // 60% alpha black overlay
                layers.PrimaryLayer().Padding(32, Unit.Point).Column(col =>
                {
                    BuildBackContent(col, r,
                        bodyColor: Colors.White,
                        accentColor: Colors.Grey.Lighten2,
                        headingFont: "Helvetica",
                        bodyFont: "Helvetica",
                        bgFillForIsbn: Colors.White);
                });
            });
        }
    }

    // -- TEMPLATE 2: Classic Centered ----------------------------------------
    private static void RenderClassicCentered(IContainer c, CoverComposeRequest r)
    {
        const string Cream = "#F5E9CF";
        const string Ink = "#1A1A1A";
        const string Accent = "#5C5141"; // warm sepia-brown

        if (r.Side == CoverSide.Front)
        {
            // Front: top 55% photo fills edge-to-edge, bottom 45% cream typography block.
            // Layers() on the image slot clips FitWidth() to the allocated height, giving
            // CSS "object-fit: cover" — no white bars on portrait images.
            c.Column(col =>
            {
                col.Item().Height(PageHeightPt * 0.55f).Layers(layers =>
                {
                    layers.Layer().Image(r.BackgroundImage).FitWidth();
                });
                col.Item().Height(PageHeightPt * 0.45f).Background(Cream)
                    .Padding(24, Unit.Point).Column(cb =>
                {
                    cb.Item().PaddingTop(20).AlignCenter().Text("✦")
                        .FontFamily("Georgia").FontSize(16).FontColor(Accent);
                    cb.Item().PaddingTop(14).AlignCenter().Text(r.Title ?? string.Empty)
                        .FontFamily("Georgia").FontSize(46).Bold().FontColor(Ink);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        cb.Item().PaddingTop(10).AlignCenter().Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(18).Italic().FontColor("#3D3530");
                    }
                    cb.Item().PaddingTop(22).AlignCenter().Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Georgia").FontSize(14).LetterSpacing(0.25f).FontColor(Accent);
                });
            });
        }
        else
        {
            // Back: full cream page — no photo strip. Think Penguin Classics, Vintage Books.
            // Content flows from top; Extend() spacer pushes footer to the bottom so the
            // ISBN box and publisher credit are always anchored at page foot.
            c.Background(Cream).Column(col =>
            {
                col.Item().Padding(38, Unit.Point).Column(inner =>
                {
                    // Double-rule border header
                    inner.Item().LineHorizontal(0.9f).LineColor(Accent);
                    inner.Item().PaddingTop(4).LineHorizontal(0.3f).LineColor(Accent);

                    // Title block
                    inner.Item().PaddingTop(22).AlignCenter().Text("✦")
                        .FontFamily("Georgia").FontSize(13).FontColor(Accent);
                    inner.Item().PaddingTop(10).AlignCenter().Text(r.Title ?? string.Empty)
                        .FontFamily("Georgia").FontSize(21).Bold().FontColor(Ink);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        inner.Item().PaddingTop(4).AlignCenter().Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(12).Italic().FontColor(Accent);
                    }

                    // Rule + synopsis
                    inner.Item().PaddingTop(16).PaddingBottom(16).LineHorizontal(0.6f).LineColor(Accent);
                    var synopsis = TruncateForBack(r.Synopsis, 500)
                        ?? "[Back-cover synopsis — fill in via the Publishing tab.]";
                    inner.Item().Text(synopsis)
                        .FontFamily("Georgia").FontSize(11).LineHeight(1.5f).FontColor(Ink).Justify();

                    // Endorsement quote (left-border style)
                    inner.Item().PaddingTop(18).PaddingLeft(10).BorderLeft(2).BorderColor(Accent)
                        .PaddingVertical(4).PaddingLeft(8)
                        .Text("\"A vivid, unforgettable read.\" — Endorsement placeholder")
                        .FontFamily("Georgia").FontSize(9.5f).Italic().FontColor(Accent);

                    // Author bio
                    inner.Item().PaddingTop(18).LineHorizontal(0.3f).LineColor(Accent);
                    inner.Item().PaddingTop(12).Text("ABOUT THE AUTHOR")
                        .FontFamily("Helvetica").FontSize(8.5f).LetterSpacing(0.3f).Bold().FontColor(Accent);
                    inner.Item().PaddingTop(4)
                        .Text(TruncateForBack(r.AuthorBio, 280)
                              ?? $"{r.AuthorPenName} writes about the things that matter.")
                        .FontFamily("Georgia").FontSize(10).LineHeight(1.4f).FontColor(Ink);
                });

                // Push footer to the absolute bottom of the page.
                col.Item().Extend();

                col.Item().PaddingHorizontal(38).PaddingBottom(32).Column(footer =>
                {
                    footer.Item().LineHorizontal(0.3f).LineColor(Accent);
                    footer.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle()
                            .Text(string.IsNullOrWhiteSpace(r.ImprintName)
                                ? "Published with MADAuthor"
                                : $"Published with {r.ImprintName}")
                            .FontFamily("Georgia").FontSize(9).Italic().FontColor(Accent);
                        row.ConstantItem(IsbnBoxWidthPt).AlignRight()
                            .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                            .Border(0.8f).BorderColor(Ink)
                            .AlignCenter().AlignMiddle()
                            .Text("ISBN").FontFamily("Helvetica").FontSize(10)
                            .LetterSpacing(0.3f).FontColor(Ink);
                    });
                    footer.Item().PaddingTop(8).LineHorizontal(0.3f).LineColor(Accent);
                    footer.Item().PaddingTop(4).LineHorizontal(0.9f).LineColor(Accent);
                });
            });
        }
    }

    // -- TEMPLATE 3: Modern Minimal ------------------------------------------
    private static void RenderModernMinimal(IContainer c, CoverComposeRequest r)
    {
        // 12% white margin → image sits in the inner 76% region.
        const float MarginPct = 0.12f;
        var marginX = PageWidthPt * MarginPct;
        var marginY = PageHeightPt * MarginPct;

        if (r.Side == CoverSide.Front)
        {
            c.Background(Colors.White).Padding(0).Layers(layers =>
            {
                layers.Layer().Column(col =>
                {
                    col.Item().Height(marginY);
                    col.Item().Row(rw =>
                    {
                        rw.ConstantItem(marginX);
                        rw.RelativeItem().Image(r.BackgroundImage).FitArea();
                        rw.ConstantItem(marginX);
                    });
                    col.Item().Height(marginY);
                });

                // Title overlay top-left of the image region.
                layers.PrimaryLayer().Padding(marginX, Unit.Point).Column(col =>
                {
                    col.Item().PaddingTop(12).Text(r.Title ?? string.Empty)
                        .FontFamily("Georgia").FontSize(40).Bold().FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        col.Item().PaddingTop(6).Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(15).Italic().FontColor(Colors.Grey.Lighten2);
                    }
                    col.Item().AlignBottom().AlignRight().PaddingBottom(8).PaddingRight(4)
                        .Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Helvetica").FontSize(12).LetterSpacing(0.2f).FontColor(Colors.White);
                });
            });
        }
        else
        {
            c.Background(Colors.White).Padding(marginX, Unit.Point).Column(col =>
            {
                BuildBackContent(col, r,
                    bodyColor: "#1A1A1A",
                    accentColor: Colors.Grey.Darken1,
                    headingFont: "Georgia",
                    bodyFont: "Georgia",
                    bgFillForIsbn: "#F4F4F4");
            });
        }
    }

    // -- TEMPLATE 4: Penguin Stripe ------------------------------------------
    private static void RenderPenguinStripe(IContainer c, CoverComposeRequest r)
    {
        // Color sampled by intent: orange (nonfiction), deep red (fiction), neutral default.
        const string Band = "#E55934";
        const string BandText = "#FFF5E1";

        if (r.Side == CoverSide.Front)
        {
            c.Column(col =>
            {
                // Top color band — title.
                col.Item().Height(PageHeightPt * 0.25f).Background(Band).Padding(20, Unit.Point)
                    .AlignCenter().AlignMiddle().Text(r.Title ?? string.Empty)
                    .FontFamily("Georgia").FontSize(38).Bold().FontColor(BandText);

                // Middle image band — Layers() clips FitWidth() overflow (CSS cover behaviour).
                col.Item().Height(PageHeightPt * 0.50f).Layers(layers =>
                {
                    layers.Layer().Image(r.BackgroundImage).FitWidth();
                });

                // Bottom color band — author.
                col.Item().Height(PageHeightPt * 0.25f).Background(Band).Padding(20, Unit.Point)
                    .AlignCenter().AlignMiddle().Column(inner =>
                {
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        inner.Item().AlignCenter().Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(15).Italic().FontColor(BandText);
                        inner.Item().PaddingTop(8);
                    }
                    inner.Item().AlignCenter().Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Helvetica").FontSize(16).LetterSpacing(0.25f).FontColor(Colors.White);
                });
            });
        }
        else
        {
            c.Column(col =>
            {
                col.Item().Height(PageHeightPt * 0.25f).Background(Band).Padding(20, Unit.Point)
                    .AlignCenter().AlignMiddle().Text("ABOUT THE BOOK")
                    .FontFamily("Helvetica").FontSize(14).LetterSpacing(0.3f).Bold().FontColor(BandText);

                col.Item().Height(PageHeightPt * 0.50f).Background("#FAF6EE").Padding(22, Unit.Point)
                    .Column(cb => BuildBackContent(cb, r,
                        bodyColor: "#1A1A1A",
                        accentColor: Band,
                        headingFont: "Georgia",
                        bodyFont: "Georgia",
                        bgFillForIsbn: "#FFFFFF",
                        showHeading: false));

                col.Item().Height(PageHeightPt * 0.25f).Background(Band).Padding(18, Unit.Point)
                    .Row(rw =>
                {
                    rw.RelativeItem().AlignMiddle().Text("Published with MADAuthor")
                        .FontFamily("Helvetica").FontSize(11).LetterSpacing(0.2f).FontColor(BandText);

                    // ISBN box (50mm x 30mm).
                    rw.ConstantItem(IsbnBoxWidthPt).AlignMiddle().AlignRight()
                        .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                        .Background(Colors.White).AlignCenter().AlignMiddle()
                        .Text("ISBN").FontFamily("Helvetica").FontSize(10)
                        .LetterSpacing(0.3f).FontColor(Colors.Grey.Darken2);
                });
            });
        }
    }

    // -- TEMPLATE 5: Magazine Block ------------------------------------------
    private static void RenderMagazineBlock(IContainer c, CoverComposeRequest r)
    {
        const string Charcoal = "#2A2A2A";

        if (r.Side == CoverSide.Front)
        {
            c.Row(row =>
            {
                // Left 55%: image — Layers() clips FitWidth() overflow (CSS cover behaviour).
                row.RelativeItem(0.55f).Layers(layers =>
                {
                    layers.Layer().Image(r.BackgroundImage).FitWidth();
                });

                // Right 45%: charcoal block with rotated title.
                row.RelativeItem(0.45f).Background(Charcoal).Layers(layers =>
                {
                    layers.PrimaryLayer().Padding(16, Unit.Point).Column(col =>
                    {
                        if (!string.IsNullOrWhiteSpace(r.Subtitle))
                        {
                            col.Item().PaddingTop(4).Text(r.Subtitle!)
                                .FontFamily("Helvetica").FontSize(13).Italic()
                                .FontColor(Colors.Grey.Lighten2);
                        }

                        // Spacer that lets the rotated title sit in the middle of the block.
                        col.Item().Height(PageHeightPt * 0.55f).AlignCenter().AlignMiddle()
                            .Rotate(-90).Text(r.Title ?? string.Empty)
                            .FontFamily("Helvetica").FontSize(48).Bold().FontColor(Colors.White);

                        col.Item().AlignBottom().AlignLeft()
                            .Text(r.AuthorPenName.ToUpperInvariant())
                            .FontFamily("Helvetica").FontSize(13).LetterSpacing(0.2f)
                            .FontColor(Colors.Grey.Lighten2);
                    });
                });
            });
        }
        else
        {
            c.Row(row =>
            {
                row.RelativeItem(0.55f).Layers(layers =>
                {
                    layers.Layer().Image(r.BackgroundImage).FitWidth();
                });
                row.RelativeItem(0.45f).Background(Charcoal).Padding(20, Unit.Point).Column(col =>
                {
                    BuildBackContent(col, r,
                        bodyColor: Colors.White,
                        accentColor: Colors.Grey.Lighten2,
                        headingFont: "Helvetica",
                        bodyFont: "Helvetica",
                        bgFillForIsbn: Colors.White);
                });
            });
        }
    }

    // -- TEMPLATE 6: Author Spotlight ----------------------------------------
    private static void RenderAuthorSpotlight(IContainer c, CoverComposeRequest r)
    {
        const string Navy = "#0F1A2E";

        if (r.Side == CoverSide.Front)
        {
            c.Column(col =>
            {
                // Top 35%: solid navy, all the typography.
                col.Item().Height(PageHeightPt * 0.35f).Background(Navy).Padding(22, Unit.Point)
                    .Column(cb =>
                {
                    cb.Item().PaddingTop(8).AlignCenter()
                        .Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Helvetica").FontSize(13).LetterSpacing(0.3f)
                        .FontColor("#9FB3D9");
                    cb.Item().PaddingTop(10).AlignCenter().Text("✦")
                        .FontFamily("Georgia").FontSize(14).FontColor("#9FB3D9");
                    cb.Item().PaddingTop(8).AlignCenter().Text(r.Title ?? string.Empty)
                        .FontFamily("Georgia").FontSize(40).Bold().FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        cb.Item().PaddingTop(8).AlignCenter().Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(16).Italic().FontColor("#C7D4EE");
                    }
                });
                // Layers() clips the FitWidth() overflow so no white bars appear for
                // landscape photos — equivalent to CSS object-fit: cover.
                col.Item().Height(PageHeightPt * 0.65f).Layers(layers =>
                {
                    layers.Layer().Image(r.BackgroundImage).FitWidth();
                });
            });
        }
        else
        {
            c.Column(col =>
            {
                col.Item().Height(PageHeightPt * 0.65f).Background("#F8F8F8").Padding(28, Unit.Point)
                    .Column(cb => BuildBackContent(cb, r,
                        bodyColor: "#1A1A1A",
                        accentColor: Navy,
                        headingFont: "Georgia",
                        bodyFont: "Georgia",
                        bgFillForIsbn: Colors.White,
                        showHeading: true));

                col.Item().Height(PageHeightPt * 0.35f).Background(Navy).Padding(22, Unit.Point)
                    .Row(rw =>
                {
                    rw.RelativeItem().AlignMiddle().Column(inner =>
                    {
                        inner.Item().Text("ABOUT THE AUTHOR")
                            .FontFamily("Helvetica").FontSize(11).LetterSpacing(0.3f).FontColor("#9FB3D9");
                        inner.Item().PaddingTop(8).Text(TruncateForBack(r.AuthorBio, 360) ??
                                "Author biography forthcoming.")
                            .FontFamily("Georgia").FontSize(10).LineHeight(1.4f).FontColor(Colors.White);
                    });

                    rw.ConstantItem(IsbnBoxWidthPt + 6).AlignBottom().AlignRight()
                        .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                        .Background(Colors.White).AlignCenter().AlignMiddle()
                        .Text("ISBN").FontFamily("Helvetica").FontSize(10)
                        .LetterSpacing(0.3f).FontColor(Colors.Grey.Darken2);
                });
            });
        }
    }

    // -- TEMPLATE 7: Night Owl -----------------------------------------------
    // Dark thriller/noir. Full-bleed image, deep shadow gradient that thickens at
    // the bottom, bold sans-serif title anchored bottom-left, electric-blue accent strip.
    private static void RenderNightOwl(IContainer c, CoverComposeRequest r)
    {
        const string Blue = "#4F8EE8";   // electric-blue accent
        const string TextDim = "#90A8CC"; // muted blue-grey for secondary text

        if (r.Side == CoverSide.Front)
        {
            c.Layers(layers =>
            {
                // Full-bleed photo.
                layers.Layer().Image(r.BackgroundImage).FitArea();

                // Two-band shadow gradient: clears at top, heavy at bottom.
                layers.Layer().Column(col =>
                {
                    col.Item().Height(PageHeightPt * 0.42f); // clear zone
                    col.Item().Height(PageHeightPt * 0.22f).Background("#4D000000"); // 30 % black
                    col.Item().Height(PageHeightPt * 0.36f).Background("#CC000000"); // 80 % black
                });

                // Title block.
                layers.PrimaryLayer().PaddingHorizontal(28).PaddingBottom(36)
                    .AlignBottom().Column(col =>
                {
                    // Thin electric-blue accent bar above the title.
                    col.Item().Width(44, Unit.Point).Height(2.5f, Unit.Point).Background(Blue);
                    col.Item().PaddingTop(12).Text(r.Title ?? string.Empty)
                        .FontFamily("Helvetica").FontSize(46).Bold().FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        col.Item().PaddingTop(6).Text(r.Subtitle!)
                            .FontFamily("Helvetica").FontSize(14).FontColor(TextDim);
                    }
                    col.Item().PaddingTop(18).Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Helvetica").FontSize(11).LetterSpacing(0.25f).FontColor(TextDim);
                });
            });
        }
        else
        {
            // Back: full-bleed image + very heavy dark overlay so text is legible.
            c.Layers(layers =>
            {
                layers.Layer().Image(r.BackgroundImage).FitArea();
                layers.Layer().Background("#E0000000"); // 88 % black overlay

                layers.PrimaryLayer().Padding(28, Unit.Point).Column(col =>
                {
                    col.Item().Width(36, Unit.Point).Height(2.5f, Unit.Point).Background(Blue);
                    col.Item().PaddingTop(12).Text(r.Title ?? string.Empty)
                        .FontFamily("Helvetica").FontSize(19).Bold().FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        col.Item().PaddingTop(3).Text(r.Subtitle!)
                            .FontFamily("Helvetica").FontSize(11).FontColor(TextDim);
                    }
                    col.Item().PaddingTop(12).LineHorizontal(0.6f).LineColor("#2A3050");

                    var synopsis = TruncateForBack(r.Synopsis, 420)
                        ?? "[Back-cover synopsis — fill in via the Publishing tab.]";
                    col.Item().PaddingTop(12).Text(synopsis)
                        .FontFamily("Helvetica").FontSize(10).LineHeight(1.4f).FontColor("#C8D4E8");

                    col.Item().PaddingTop(14).Text("\"A vivid, unforgettable read.\" — Endorsement placeholder")
                        .FontFamily("Helvetica").FontSize(9).Italic().FontColor(TextDim);

                    col.Item().PaddingTop(14).Text("ABOUT THE AUTHOR")
                        .FontFamily("Helvetica").FontSize(8.5f).LetterSpacing(0.3f).Bold().FontColor(Blue);
                    col.Item().PaddingTop(4)
                        .Text(TruncateForBack(r.AuthorBio, 220)
                              ?? $"{r.AuthorPenName} writes about the things that matter.")
                        .FontFamily("Helvetica").FontSize(9.5f).LineHeight(1.35f).FontColor(TextDim);

                    col.Item().Extend();

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().AlignMiddle()
                            .Text(string.IsNullOrWhiteSpace(r.ImprintName)
                                ? "Published with MADAuthor"
                                : $"Published with {r.ImprintName}")
                            .FontFamily("Helvetica").FontSize(8.5f).FontColor("#4A5A8A");
                        row.ConstantItem(IsbnBoxWidthPt).AlignRight()
                            .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                            .Background("#0A0A14").Border(0.8f).BorderColor(Blue)
                            .AlignCenter().AlignMiddle()
                            .Text("ISBN").FontFamily("Helvetica").FontSize(10)
                            .LetterSpacing(0.3f).FontColor(Colors.White);
                    });
                });
            });
        }
    }

    // -- TEMPLATE 8: Golden Age ----------------------------------------------
    // Warm vintage literary. Aged paper back, ornamental double-rule border,
    // sepia image overlay on front, classic Georgia serif throughout.
    private static void RenderGoldenAge(IContainer c, CoverComposeRequest r)
    {
        const string Paper = "#F2E5C0";  // aged paper
        const string Sepia = "#5A3A1A";  // dark sepia ink
        const string Gold  = "#8B6914";  // antique gold accent

        if (r.Side == CoverSide.Front)
        {
            c.Layers(layers =>
            {
                // Full-bleed photo.
                layers.Layer().Image(r.BackgroundImage).FitArea();

                // Warm amber-sepia colour wash over the photo — simulates aged film.
                // ARGB: alpha=55 (33%), R=C0, G=80, B=20 → warm amber-brown.
                layers.Layer().Background("#55C08020");

                // Heavy vignette at the edges: dark sepia band at bottom 40%.
                layers.Layer().Column(col =>
                {
                    col.Item().Height(PageHeightPt * 0.60f);
                    col.Item().Height(PageHeightPt * 0.20f).Background("#59000000"); // 35 % black
                    col.Item().Height(PageHeightPt * 0.20f).Background("#B3000000"); // 70 % black
                });

                // Decorative inset border frame.
                layers.Layer().Padding(14, Unit.Point)
                    .Border(1.2f).BorderColor("#80C8A060"); // semi-transparent gold border

                // Title block at bottom.
                layers.PrimaryLayer().PaddingHorizontal(28).PaddingBottom(30)
                    .AlignBottom().Column(col =>
                {
                    col.Item().AlignCenter().Text("✦  ✦  ✦")
                        .FontFamily("Georgia").FontSize(11).FontColor("#D4B070").LetterSpacing(0.2f);
                    col.Item().PaddingTop(12).AlignCenter().Text(r.Title ?? string.Empty)
                        .FontFamily("Georgia").FontSize(42).Bold().FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        col.Item().PaddingTop(8).AlignCenter().Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(16).Italic().FontColor("#E8D090");
                    }
                    col.Item().PaddingTop(20).AlignCenter().Text(r.AuthorPenName.ToUpperInvariant())
                        .FontFamily("Georgia").FontSize(13).LetterSpacing(0.22f).FontColor("#D4B070");
                });
            });
        }
        else
        {
            // Back: full aged-paper page, ornamental double border, serif typography.
            c.Background(Paper).Column(col =>
            {
                col.Item().Padding(30, Unit.Point).Column(inner =>
                {
                    // Ornamental double-rule border at top.
                    inner.Item().LineHorizontal(1.2f).LineColor(Gold);
                    inner.Item().PaddingTop(5).LineHorizontal(0.4f).LineColor(Gold);
                    inner.Item().PaddingTop(5).LineHorizontal(0.4f).LineColor(Gold);

                    // Title block.
                    inner.Item().PaddingTop(18).AlignCenter().Text("✦  ✦  ✦")
                        .FontFamily("Georgia").FontSize(11).FontColor(Gold);
                    inner.Item().PaddingTop(10).AlignCenter().Text(r.Title ?? string.Empty)
                        .FontFamily("Georgia").FontSize(20).Bold().FontColor(Sepia);
                    if (!string.IsNullOrWhiteSpace(r.Subtitle))
                    {
                        inner.Item().PaddingTop(4).AlignCenter().Text(r.Subtitle!)
                            .FontFamily("Georgia").FontSize(12).Italic().FontColor(Gold);
                    }

                    // Rule + synopsis.
                    inner.Item().PaddingTop(16).PaddingBottom(16).LineHorizontal(0.6f).LineColor(Gold);
                    var synopsis = TruncateForBack(r.Synopsis, 500)
                        ?? "[Back-cover synopsis — fill in via the Publishing tab.]";
                    inner.Item().Text(synopsis)
                        .FontFamily("Georgia").FontSize(11).LineHeight(1.5f).FontColor(Sepia).Justify();

                    // Endorsement.
                    inner.Item().PaddingTop(16).PaddingLeft(10).BorderLeft(2).BorderColor(Gold)
                        .PaddingVertical(4).PaddingLeft(8)
                        .Text("\"A vivid, unforgettable read.\" — Endorsement placeholder")
                        .FontFamily("Georgia").FontSize(9.5f).Italic().FontColor(Gold);

                    // Author bio.
                    inner.Item().PaddingTop(16).LineHorizontal(0.4f).LineColor(Gold);
                    inner.Item().PaddingTop(12).Text("ABOUT THE AUTHOR")
                        .FontFamily("Helvetica").FontSize(8.5f).LetterSpacing(0.3f).Bold().FontColor(Gold);
                    inner.Item().PaddingTop(4)
                        .Text(TruncateForBack(r.AuthorBio, 280)
                              ?? $"{r.AuthorPenName} writes about the things that matter.")
                        .FontFamily("Georgia").FontSize(10).LineHeight(1.4f).FontColor(Sepia);
                });

                // Push footer to the bottom of the page.
                col.Item().Extend();

                col.Item().PaddingHorizontal(30).PaddingBottom(28).Column(footer =>
                {
                    footer.Item().LineHorizontal(0.4f).LineColor(Gold);
                    footer.Item().PaddingTop(5).LineHorizontal(0.4f).LineColor(Gold);
                    footer.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle()
                            .Text(string.IsNullOrWhiteSpace(r.ImprintName)
                                ? "Published with MADAuthor"
                                : $"Published with {r.ImprintName}")
                            .FontFamily("Georgia").FontSize(9).Italic().FontColor(Gold);
                        row.ConstantItem(IsbnBoxWidthPt).AlignRight()
                            .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                            .Background(Paper).Border(0.8f).BorderColor(Gold)
                            .AlignCenter().AlignMiddle()
                            .Text("ISBN").FontFamily("Helvetica").FontSize(10)
                            .LetterSpacing(0.3f).FontColor(Sepia);
                    });
                    footer.Item().PaddingTop(8).LineHorizontal(0.4f).LineColor(Gold);
                    footer.Item().PaddingTop(5).LineHorizontal(1.2f).LineColor(Gold);
                });
            });
        }
    }

    // -- Shared back-cover content block -------------------------------------

    // 50mm × 30mm in PDF points (1 mm ≈ 2.8346 pt).
    private const float IsbnBoxWidthPt = 50f * 2.8346f;   // ~141.7 pt
    private const float IsbnBoxHeightPt = 30f * 2.8346f;  // ~85.0 pt

    private static void BuildBackContent(
        ColumnDescriptor col,
        CoverComposeRequest r,
        string bodyColor,
        string accentColor,
        string headingFont,
        string bodyFont,
        string bgFillForIsbn,
        bool showHeading = true)
    {
        // Back-panel content budgets are deliberately conservative. The container we're
        // composed into is typically ~5.5" tall after padding (some templates eat half
        // the height with an image). Anything more aggressive than this and QuestPDF
        // throws DocumentLayoutException with the unhelpful generic "Page content
        // overflow" error - which is the root cause of the "Could not compose back of
        // cover" toast users were seeing. Hard caps below were tuned empirically:
        //  * synopsis 420 chars at 10pt/line-height 1.35 fits in ~140pt
        //  * bio 240 chars at 9.5pt/line-height 1.35 fits in ~70pt
        //  * heading + rule + endorsement + footer + ISBN box ≈ 220pt
        // Total ≈ 430pt - leaves a healthy margin under the 5.5" panel.
        if (showHeading)
        {
            col.Item().Text(r.Title ?? string.Empty)
                .FontFamily(headingFont).FontSize(18).Bold().FontColor(bodyColor);
            if (!string.IsNullOrWhiteSpace(r.Subtitle))
            {
                col.Item().PaddingTop(2).Text(r.Subtitle!)
                    .FontFamily(headingFont).FontSize(11).Italic().FontColor(accentColor);
            }
            col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor(accentColor);
        }

        // Synopsis (or placeholder so we never feed QuestPDF an empty string).
        var synopsis = TruncateForBack(r.Synopsis, 420)
            ?? "[Back-cover synopsis - fill in via the Publishing tab.]";
        col.Item().PaddingTop(10).Text(synopsis)
            .FontFamily(bodyFont).FontSize(10).LineHeight(1.35f).FontColor(bodyColor);

        // Endorsement placeholder - single line so it never wraps to two and overflows.
        col.Item().PaddingTop(10).Text(
            "\"A vivid, unforgettable read.\" - Endorsement placeholder")
            .FontFamily(bodyFont).FontSize(9).Italic().FontColor(accentColor);

        // Author bio.
        col.Item().PaddingTop(10).Text("ABOUT THE AUTHOR")
            .FontFamily(headingFont).FontSize(9).LetterSpacing(0.3f).Bold().FontColor(accentColor);
        col.Item().PaddingTop(2).Text(TruncateForBack(r.AuthorBio, 240) ??
                $"{r.AuthorPenName} writes about the things that matter.")
            .FontFamily(bodyFont).FontSize(9.5f).LineHeight(1.35f).FontColor(bodyColor);

        // Footer + ISBN row.
        col.Item().PaddingTop(12).Row(row =>
        {
            row.RelativeItem().AlignMiddle().Text(
                string.IsNullOrWhiteSpace(r.ImprintName)
                    ? "Published with MADAuthor"
                    : $"Published with {r.ImprintName}")
                .FontFamily(bodyFont).FontSize(8.5f).Italic().FontColor(accentColor);

            row.ConstantItem(IsbnBoxWidthPt).AlignRight()
                .Width(IsbnBoxWidthPt).Height(IsbnBoxHeightPt)
                .Background(bgFillForIsbn)
                .Border(0.5f).BorderColor(Colors.Grey.Medium)
                .AlignCenter().AlignMiddle()
                .Text("ISBN").FontFamily(bodyFont).FontSize(10)
                .LetterSpacing(0.3f).FontColor(Colors.Grey.Darken2);
        });
    }

    private static string? TruncateForBack(string? raw, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var stripped = StripMarkupForBack(raw.Trim());
        if (string.IsNullOrWhiteSpace(stripped)) return null;
        if (stripped.Length <= maxChars) return stripped;
        return stripped[..maxChars].TrimEnd() + "…";
    }

    private static string StripMarkupForBack(string text)
    {
        // Replace block-level closing tags with a space so adjacent words don't merge.
        var s = System.Text.RegularExpressions.Regex.Replace(
            text, @"</?(p|div|br|li|h[1-6])\b[^>]*>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Strip remaining HTML tags.
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", string.Empty);
        // Decode HTML entities (&amp; &lt; &nbsp; etc.).
        s = System.Net.WebUtility.HtmlDecode(s);
        // Strip markdown bold/italic markers.
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\*{1,3}|_{1,3}", string.Empty);
        // Collapse whitespace.
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }
}
