using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace MetadataTagging.Services;

/// <summary>
/// Extracts paragraphs from plain-text, Markdown, or PDF streams.
/// </summary>
public sealed class TextChunkService
{
    private static readonly string[] ParagraphSeparators =
        ["\r\n\r\n", "\n\n"];

    /// <summary>
    /// Reads <paramref name="textStream"/> as UTF-8 text, splits on blank lines,
    /// and returns a list of non-empty paragraph strings.
    /// </summary>
    public IReadOnlyList<string> ExtractParagraphs(Stream textStream)
    {
        using var reader = new StreamReader(textStream, leaveOpen: false);
        var text = reader.ReadToEnd();

        return text
            .Split(ParagraphSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Reads <paramref name="pdfStream"/> as a PDF and returns its text blocks, page by page,
    /// as paragraphs with each block's lines joined by spaces.
    /// </summary>
    /// <exception cref="InvalidDataException">The stream is not a readable PDF (corrupt or encrypted).</exception>
    public async Task<IReadOnlyList<string>> ExtractPdfParagraphsAsync(Stream pdfStream, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, ct);

        try
        {
            using var document = PdfDocument.Open(buffer.ToArray());
            var paragraphs = new List<string>();
            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
                paragraphs.AddRange(DocstrumBoundingBoxes.Instance.GetBlocks(words)
                    .Select(b => string.Join(' ', b.TextLines.Select(l => l.Text.Trim())).Trim())
                    .Where(p => p.Length > 0));
            }
            return paragraphs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("The file could not be read as a PDF.", ex);
        }
    }
}
