using System.Text.Json.Serialization;

namespace MadAuthor.Application.Covers;

// JsonConverter attribute on the enum itself means System.Text.Json accepts both the
// string name ("BoldGradient") and the integer (0) on input, and emits the string name
// on output. The SPA sends/expects strings; without this, the controller binders for
// DesignRequest / WrapRequest reject the SPA's payload with HTTP 400 before the action
// runs. Scoped to these two enums rather than registered globally - other DTO enums
// (BookProjectStatus, WorkflowStage, BookChapterStatus) must stay as integers because
// the SPA's TS types are numeric-literal unions with integer-keyed label maps.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CoverTemplate
{
    BoldGradient,
    ClassicCentered,
    ModernMinimal,
    PenguinStripe,
    MagazineBlock,
    AuthorSpotlight,
    /// <summary>Dark dramatic thriller/noir aesthetic. Full-bleed image with deep shadow gradient and electric-blue accent.</summary>
    NightOwl,
    /// <summary>Warm vintage literary aesthetic. Aged paper back, ornamental border, classic serif typography.</summary>
    GoldenAge,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CoverSide
{
    Front,
    Back,
}

public interface ICoverComposer
{
    /// <summary>Compose a designed cover panel (front or back) by overlaying typography
    /// on the supplied background image. Returns PNG bytes at print resolution (300 DPI,
    /// 6x9 = 1800x2700 px).</summary>
    Task<byte[]> ComposePanelAsync(CoverComposeRequest request, CancellationToken ct = default);

    /// <summary>Render a print-ready cover-wrap PDF (front + spine + back, with optional
    /// bleed). Returns PDF bytes for upload to KDP / Ingram.</summary>
    Task<byte[]> RenderWrapAsync(CoverWrapRequest request, CancellationToken ct = default);
}

public sealed record CoverComposeRequest(
    byte[] BackgroundImage,
    CoverTemplate Template,
    CoverSide Side,
    string Title,
    string? Subtitle,
    string AuthorPenName,
    // Back-cover content (ignored for Side=Front)
    string? Synopsis = null,
    string? AuthorBio = null,
    string? ImprintName = null);

public sealed record CoverWrapRequest(
    byte[] FrontPanelPng,    // composed front bytes (already designed)
    byte[] BackPanelPng,     // composed back bytes
    string SpineTitle,       // usually book Title
    string SpineAuthor,
    int PageCount,
    double SpineWidthInches, // pre-computed by caller (page-count x paper-weight)
    bool IngramBleed = false);
