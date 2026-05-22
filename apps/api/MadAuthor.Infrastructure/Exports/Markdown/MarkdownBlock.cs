namespace MadAuthor.Infrastructure.Exports.Markdown;

/// <summary>A flat block representation we render into PDF and DOCX without
/// each renderer having to walk a full Markdown AST.
///
/// Blocks carry both a legacy flattened <c>Text</c> (plain string, bold/italic stripped)
/// AND an optional <c>Runs</c> tree that preserves inline emphasis. Renderers should
/// prefer <c>Runs</c> when present and fall back to <c>Text</c> otherwise.</summary>
public abstract record MarkdownBlock;

public sealed record HeadingBlock(int Level, string Text) : MarkdownBlock
{
    public IReadOnlyList<InlineRun>? Runs { get; init; }
}

public sealed record ParagraphBlock(string Text) : MarkdownBlock
{
    public IReadOnlyList<InlineRun>? Runs { get; init; }
}

public sealed record BulletItemBlock(string Text) : MarkdownBlock
{
    public IReadOnlyList<InlineRun>? Runs { get; init; }
}

public sealed record NumberedItemBlock(int Index, string Text) : MarkdownBlock
{
    public IReadOnlyList<InlineRun>? Runs { get; init; }
}

public sealed record QuoteBlock(string Text) : MarkdownBlock
{
    public IReadOnlyList<InlineRun>? Runs { get; init; }
}

public sealed record CodeBlock(string Text) : MarkdownBlock;
public sealed record ThematicBreakBlock : MarkdownBlock;

/// <summary>Inline span model. Preserves bold/italic/code/link from Markdown
/// so high-fidelity renderers (PDF, DOCX, EPUB) can apply formatting.</summary>
public abstract record InlineRun;
public sealed record TextRun(string Text) : InlineRun;
public sealed record BoldRun(IReadOnlyList<InlineRun> Children) : InlineRun;
public sealed record ItalicRun(IReadOnlyList<InlineRun> Children) : InlineRun;
public sealed record CodeRun(string Text) : InlineRun;
public sealed record LinkRun(string Text, string? Url) : InlineRun;
public sealed record SoftBreakRun : InlineRun;
