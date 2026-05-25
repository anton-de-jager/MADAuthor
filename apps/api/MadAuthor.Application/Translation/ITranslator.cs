namespace MadAuthor.Application.Translation;

/// <summary>
/// Provider-agnostic Markdown translation contract. Implementations (OpenAI gpt-4o-mini,
/// DeepL, NoOp) are wired in <c>TranslationDependencyInjection</c> based on which API key
/// is set in the environment. The selected provider is auto-detected once at startup;
/// toggling providers requires an env-var change + restart.
/// </summary>
public interface ITranslator
{
    /// <summary>True when a real provider is wired (i.e. an API key is configured).</summary>
    bool IsEnabled { get; }

    /// <summary>Display name of the active provider (e.g. "openai-gpt-4o-mini", "deepl"), or "none".</summary>
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
