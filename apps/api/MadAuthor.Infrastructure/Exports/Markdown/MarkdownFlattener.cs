using Markdig;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax;

namespace MadAuthor.Infrastructure.Exports.Markdown;

/// <summary>
/// Parses Markdown via Markdig and flattens it into a sequence of <see cref="MarkdownBlock"/>s
/// renderers can walk. Each emphasis-bearing block carries both the legacy <c>Text</c>
/// (plain-string flattened) and a richer <c>Runs</c> tree so renderers can preserve
/// bold/italic/code/link formatting.
/// </summary>
public static class MarkdownFlattener
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static IReadOnlyList<MarkdownBlock> Flatten(string markdown)
    {
        var doc = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);
        return FlattenCore(doc);
    }

    /// <summary>Flatten markdown for renderers that draw a chapter title themselves
    /// (PDF chapter opening). Drops a leading H1 whose text equals <paramref name="chapterTitle"/>
    /// (case-insensitive, trimmed) so the title isn't rendered twice. Other consumers
    /// (DOCX/EPUB) should keep using <see cref="Flatten"/>.</summary>
    public static IReadOnlyList<MarkdownBlock> FlattenStrippingTitleH1(string markdown, string? chapterTitle)
    {
        var all = Flatten(markdown);
        if (all.Count == 0 || string.IsNullOrWhiteSpace(chapterTitle)) return all;
        if (all[0] is HeadingBlock h && h.Level == 1)
        {
            var headText = (h.Text ?? string.Empty).Trim();
            if (string.Equals(headText, chapterTitle.Trim(), StringComparison.OrdinalIgnoreCase))
                return all.Skip(1).ToList();
        }
        return all;
    }

    private static IReadOnlyList<MarkdownBlock> FlattenCore(MdBlock.MarkdownDocument doc)
    {
        var blocks = new List<MarkdownBlock>();

        foreach (var block in doc)
        {
            switch (block)
            {
                case MdBlock.HeadingBlock h:
                {
                    var runs = BuildRuns(h.Inline);
                    blocks.Add(new HeadingBlock(h.Level, GetInlineText(h.Inline)) { Runs = runs });
                    break;
                }
                case MdBlock.ParagraphBlock p:
                {
                    var runs = BuildRuns(p.Inline);
                    blocks.Add(new ParagraphBlock(GetInlineText(p.Inline)) { Runs = runs });
                    break;
                }
                case MdBlock.ListBlock list:
                {
                    var i = 1;
                    foreach (var child in list)
                    {
                        if (child is not MdBlock.ListItemBlock item) continue;
                        var paras = item.OfType<MdBlock.ParagraphBlock>().ToList();
                        var text = string.Join(" ", paras.Select(p => GetInlineText(p.Inline)));
                        var runs = CombineRuns(paras.Select(p => BuildRuns(p.Inline)));
                        if (list.IsOrdered)
                            blocks.Add(new NumberedItemBlock(i++, text) { Runs = runs });
                        else
                            blocks.Add(new BulletItemBlock(text) { Runs = runs });
                    }
                    break;
                }
                case MdBlock.QuoteBlock q:
                {
                    var qParas = q.OfType<MdBlock.ParagraphBlock>().ToList();
                    var qText = string.Join(" ", qParas.Select(p => GetInlineText(p.Inline)));
                    var qRuns = CombineRuns(qParas.Select(p => BuildRuns(p.Inline)));
                    blocks.Add(new QuoteBlock(qText) { Runs = qRuns });
                    break;
                }
                case MdBlock.FencedCodeBlock fenced:
                    blocks.Add(new CodeBlock(fenced.Lines.ToString()));
                    break;
                case MdBlock.CodeBlock code:
                    blocks.Add(new CodeBlock(code.Lines.ToString()));
                    break;
                case MdBlock.ThematicBreakBlock:
                    blocks.Add(new ThematicBreakBlock());
                    break;
            }
        }

        return blocks;
    }

    // ----- legacy plain-text flattening (preserved) ------------------------

    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var child in inline)
        {
            switch (child)
            {
                case LiteralInline li: sb.Append(li.Content); break;
                case EmphasisInline em: sb.Append(GetInlineText(em)); break;
                case CodeInline ci: sb.Append(ci.Content); break;
                case LinkInline li: sb.Append(GetInlineText(li)); break;
                case LineBreakInline: sb.Append(' '); break;
                case ContainerInline c: sb.Append(GetInlineText(c)); break;
                default: sb.Append(child.ToString()); break;
            }
        }
        return sb.ToString().Trim();
    }

    // ----- richer inline-run tree ------------------------------------------

    /// <summary>Walks Markdig's inline tree and produces a list of <see cref="InlineRun"/>s,
    /// recursively nesting Bold/Italic. Returns null when the container is empty.</summary>
    private static IReadOnlyList<InlineRun>? BuildRuns(ContainerInline? inline)
    {
        if (inline is null) return null;
        var list = new List<InlineRun>();
        AppendRuns(inline, list);
        return list.Count == 0 ? null : list;
    }

    private static void AppendRuns(ContainerInline container, List<InlineRun> output)
    {
        foreach (var child in container)
        {
            switch (child)
            {
                case LiteralInline li:
                    if (li.Content.Length > 0)
                        output.Add(new TextRun(li.Content.ToString()));
                    break;
                case EmphasisInline em:
                {
                    var inner = new List<InlineRun>();
                    AppendRuns(em, inner);
                    if (inner.Count == 0) break;
                    // DelimiterCount == 2 → strong (bold); 1 → emphasis (italic);
                    // 3 → bold+italic which we nest.
                    if (em.DelimiterCount >= 3)
                        output.Add(new BoldRun(new List<InlineRun> { new ItalicRun(inner) }));
                    else if (em.DelimiterCount == 2)
                        output.Add(new BoldRun(inner));
                    else
                        output.Add(new ItalicRun(inner));
                    break;
                }
                case CodeInline ci:
                    output.Add(new CodeRun(ci.Content));
                    break;
                case LinkInline link:
                {
                    var inner = new List<InlineRun>();
                    AppendRuns(link, inner);
                    var text = string.Concat(inner.OfType<TextRun>().Select(t => t.Text));
                    if (string.IsNullOrEmpty(text))
                        text = link.Url ?? string.Empty;
                    output.Add(new LinkRun(text, link.Url));
                    break;
                }
                case LineBreakInline lb:
                    output.Add(new SoftBreakRun());
                    break;
                case ContainerInline c:
                    AppendRuns(c, output);
                    break;
                default:
                {
                    var s = child.ToString();
                    if (!string.IsNullOrEmpty(s))
                        output.Add(new TextRun(s));
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<InlineRun>? CombineRuns(IEnumerable<IReadOnlyList<InlineRun>?> parts)
    {
        var combined = new List<InlineRun>();
        var first = true;
        foreach (var p in parts)
        {
            if (p is null || p.Count == 0) continue;
            if (!first) combined.Add(new TextRun(" "));
            combined.AddRange(p);
            first = false;
        }
        return combined.Count == 0 ? null : combined;
    }

    /// <summary>Flatten an inline-run tree into plain text (used by helpers that don't need formatting).</summary>
    public static string RunsToPlainText(IReadOnlyList<InlineRun>? runs)
    {
        if (runs is null || runs.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var r in runs) AppendPlain(r, sb);
        return sb.ToString();
    }

    private static void AppendPlain(InlineRun run, System.Text.StringBuilder sb)
    {
        switch (run)
        {
            case TextRun t: sb.Append(t.Text); break;
            case BoldRun b: foreach (var c in b.Children) AppendPlain(c, sb); break;
            case ItalicRun i: foreach (var c in i.Children) AppendPlain(c, sb); break;
            case CodeRun c: sb.Append(c.Text); break;
            case LinkRun l: sb.Append(l.Text); break;
            case SoftBreakRun: sb.Append(' '); break;
        }
    }
}
