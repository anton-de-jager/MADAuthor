using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MadAuthor.Application.Ingestion;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace MadAuthor.Infrastructure.Ingestion;

public class TextExtractor(
    ILogger<TextExtractor> log,
    IAudioTranscriber audio,
    IOcrService ocr) : ITextExtractor
{
    // PDFs that hand back fewer than this many characters are almost certainly scanned -
    // PdfPig can't pull text out of a rasterized page. We log the case so it's visible, but we
    // don't attempt page rasterization here (out of scope for this phase - see Phase 6).
    private const int ScannedPdfTextThreshold = 50;

    public async Task<string?> ExtractAsync(
        string mimeType, string fileName, Stream content, CancellationToken ct = default)
    {
        var mime = (mimeType ?? string.Empty).ToLowerInvariant();
        try
        {
            // Audio MIME types - delegate to the transcriber. The DI layer wires up either the
            // MADCloud-only transcription boundary or a NoOp that returns null.
            if (mime.StartsWith("audio/", StringComparison.Ordinal))
            {
                if (!audio.IsEnabled)
                {
                    log.LogDebug("Audio upload {File} stored without transcription (provider disabled).", fileName);
                    return null;
                }
                var result = await audio.TranscribeAsync(content, fileName, mime, ct);
                if (result is null) return null;
                log.LogInformation(
                    "Transcribed {File} via {Provider} ({Chars} chars, lang={Lang}).",
                    fileName, result.Provider, result.Text.Length, result.Language ?? "unknown");
                return result.Text;
            }

            // Image MIME types - delegate to the OCR service. Same gating pattern as audio.
            if (mime.StartsWith("image/", StringComparison.Ordinal))
            {
                if (!ocr.IsEnabled)
                {
                    log.LogDebug("Image upload {File} stored without OCR (provider disabled).", fileName);
                    return null;
                }
                var text = await ocr.ExtractTextAsync(content, fileName, mime, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    log.LogInformation(
                        "OCR'd {File} via {Provider} ({Chars} chars).",
                        fileName, ocr.ProviderName, text.Length);
                }
                return text;
            }

            return mime switch
            {
                "text/plain" or "text/markdown" => await ReadTextAsync(content, ct),
                "application/pdf" => await ReadPdfAsync(fileName, content, ct),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ReadDocx(content),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Text extraction failed for {File} ({Mime})", fileName, mimeType);
            return null;
        }
    }

    private static async Task<string> ReadTextAsync(Stream s, CancellationToken ct)
    {
        using var reader = new StreamReader(s, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private async Task<string> ReadPdfAsync(string fileName, Stream s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        using (var doc = PdfDocument.Open(s))
        {
            foreach (var page in doc.GetPages())
            {
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }
        }
        var extracted = sb.ToString().Trim();

        // Likely a scanned PDF - PdfPig couldn't find an embedded text layer. We don't currently
        // rasterize PDF pages and re-OCR them (see Phase 6); flag the case in logs so an operator
        // can convert the file manually or re-upload page images. The OCR service is still passed
        // in so we can light up rasterization later without re-touching the controller.
        if (extracted.Length < ScannedPdfTextThreshold)
        {
            log.LogInformation(
                "PDF {File} yielded only {Chars} chars of embedded text - likely a scanned/image PDF. " +
                "OCR fallback for PDFs is not enabled in this build; upload page images directly or " +
                "wait for the Phase 6 rasterizer.",
                fileName, extracted.Length);
            _ = ocr; // explicit reference to keep the DI dependency live for the upcoming Phase 6 path
            await Task.CompletedTask; // keep the method async for symmetry with the audio/image branches
        }

        return extracted;
    }

    private static string ReadDocx(Stream s)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(s, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            sb.AppendLine(paragraph.InnerText);
        }
        return sb.ToString().Trim();
    }
}
