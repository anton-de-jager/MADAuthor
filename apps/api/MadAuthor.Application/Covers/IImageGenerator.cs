namespace MadAuthor.Application.Covers;

/// <summary>
/// Provider-agnostic image generation contract used by the cover-AI endpoint. Implementations
/// (OpenAI DALL-E, Stability, NoOp) are wired in <c>ImageGenDependencyInjection</c> based on
/// which API key is set in the environment. The selected provider is auto-detected once at
/// startup; toggling providers requires an env-var change + restart.
/// </summary>
public interface IImageGenerator
{
    /// <summary>True when a real provider is wired (i.e. an API key is configured).</summary>
    bool IsEnabled { get; }

    /// <summary>Display name of the active provider (e.g. "openai-dall-e-3", "stability-sd3"), or "none".</summary>
    string ProviderName { get; }

    /// <summary>
    /// Generate one cover image. Returns PNG bytes + the prompt that was actually used (after any
    /// provider-side rewriting, e.g. DALL-E's automatic prompt revision) + attribution metadata
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
