namespace MadAuthor.Application.Covers;

/// <summary>
/// Provider-agnostic image generation contract used by the cover-AI endpoint. MADAuthor
/// routes AI image generation through MADCloud only; local implementations never call AI
/// vendors directly.
/// </summary>
public interface IImageGenerator
{
    /// <summary>True when a local provider is wired. MADCloud-only mode returns false locally.</summary>
    bool IsEnabled { get; }

    /// <summary>Display name of the active provider boundary.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Generate one cover image. Returns PNG bytes + the prompt that was actually used (after any
    /// provider-side rewriting) + attribution metadata
    /// for storage. Returns null when the provider is disabled or the call failed in a way the
    /// caller should surface (the implementation logs the underlying error).
    /// </summary>
    Task<GeneratedImage?> GenerateAsync(GenerateImageRequest request, CancellationToken ct = default);
}

public sealed record GenerateImageRequest(
    string Prompt,             // human-readable prompt the user/system asked for
    int Width = 1024,
    int Height = 1792,         // 9:16 portrait, suits book-cover aspect
    string? StyleHint = null,  // e.g. "oil painting", "minimalist", "photographic"
    string? NegativePrompt = null);

public sealed record GeneratedImage(
    byte[] PngBytes,
    string PromptUsed,
    string Provider,           // "openai-dall-e-3", etc.
    string? ModelVersion = null);
