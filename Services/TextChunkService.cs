namespace MetadataTagging.Services;

/// <summary>
/// Extracts paragraphs from a plain-text or Markdown stream by splitting on blank lines.
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
}
