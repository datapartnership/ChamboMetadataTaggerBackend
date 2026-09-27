using MetadataTagging.Services;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MetadataTagging.Tests;

public class TextServingTests
{
    [Fact]
    public async Task ExtractPdfParagraphs_ReturnsTextBlocksFromEveryPage()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page1 = builder.AddPage(595, 842);
        page1.AddText("Malaria cases fell sharply", 12, new(72, 700), font);
        page1.AddText("across the northern region.", 12, new(72, 686), font);
        page1.AddText("Schools reopened in March.", 12, new(72, 450), font);
        builder.AddPage(595, 842).AddText("Annex with district tables.", 12, new(72, 700), font);

        var paragraphs = await new TextChunkService().ExtractPdfParagraphsAsync(new MemoryStream(builder.Build()));

        Assert.Equal(
            ["Annex with district tables.", "Malaria cases fell sharply across the northern region.", "Schools reopened in March."],
            paragraphs.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExtractPdfParagraphs_ThrowsInvalidDataForNonPdf()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TextChunkService().ExtractPdfParagraphsAsync(new MemoryStream("not a pdf"u8.ToArray())));
    }

    [Theory]
    [InlineData("bytes=0-99", 1000, true, 0, 99)]
    [InlineData("bytes=900-", 1000, true, 900, 999)]
    [InlineData("bytes=-100", 1000, true, 900, 999)]
    [InlineData("bytes=500-5000", 1000, true, 500, 999)]
    [InlineData("bytes=1000-", 1000, false, 0, 0)]
    [InlineData("bytes=9-3", 1000, false, 0, 0)]
    [InlineData("items=0-1", 1000, false, 0, 0)]
    public void TryParseRange_HandlesSupportedForms(string header, long size, bool ok, long start, long end)
    {
        Assert.Equal(ok, StorageStreaming.TryParseRange(header, size, out var s, out var e));
        if (ok)
            Assert.Equal((start, end), (s, e));
    }
}
