namespace MadAuthor.Application.Translation;

/// <summary>
/// Provider-agnostic Markdown translation contract. MADAuthor does not call AI vendors
/// directly; translation work is routed through MADCloud and this interface remains the
/// local boundary used by controllers.
/// </summary>
public interface ITranslator
{
    /// <summary>True when a local provider is wired. MADCloud-only mode returns false locally.</summary>
    bool IsEnabled { get; }

    /// <summary>Display name of the active provider boundary.</summary>
    string ProviderName { get; }

    /// <summary>Translate Markdown content from one language to another. Preserves headings, lists, emphasis, code blocks, and quotes.</summary>
    Task<TranslationResult?> TranslateAsync(TranslateRequest request, CancellationToken ct = default);
}

public sealed record TranslateRequest(
    string SourceMarkdown,
    string TargetLanguage,         // ISO 639-1 ("es", "fr", "de") or full name ("Spanish")
    string? SourceLanguage = null, // null = auto-detect
    string? StyleHint = null);     // e.g. "warm, conversational" - passed into the prompt

public sealed record TranslationResult(string TranslatedMarkdown, string SourceLanguageDetected, string Provider);
