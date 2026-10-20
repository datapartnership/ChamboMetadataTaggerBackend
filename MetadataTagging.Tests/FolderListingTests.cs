using MetadataTagging.Services;

namespace MetadataTagging.Tests;

public class FolderListingTests
{
    private static readonly string[] Keys =
    [
        "ProvA/Col1/a.wav",
        "ProvA/Col1/b.wav",
        "ProvA/Col2/c.wav",
        "ProvB/Col1/sub/d.wav",
        "ProvC/Col1/e.wav",
        "root.wav",
    ];

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("ProvA", "ProvA/")]
    [InlineData("ProvA/Col1/", "ProvA/Col1/")]
    [InlineData("\\ProvA\\Col1", "ProvA/Col1/")]
    public void NormalizeFolder_AddsSingleTrailingSlash(string? input, string expected) =>
        Assert.Equal(expected, FolderListing.NormalizeFolder(input));

    [Fact]
    public void Split_AtRoot_ReturnsEveryTopLevelFolderWithCounts()
    {
        var (folders, files) = FolderListing.Split(Keys, k => k, "");

        Assert.Equal(["ProvA", "ProvB", "ProvC"], folders.Select(f => f.Name));
        Assert.Equal(["ProvA/", "ProvB/", "ProvC/"], folders.Select(f => f.Path));
        Assert.Equal([3, 1, 1], folders.Select(f => f.FileCount));
        Assert.Equal(["root.wav"], files);
    }

    [Fact]
    public void Split_InSubFolder_ReturnsOnlyItsChildren()
    {
        var (folders, files) = FolderListing.Split(Keys, k => k, "ProvA/Col1/");

        Assert.Empty(folders);
        Assert.Equal(["ProvA/Col1/a.wav", "ProvA/Col1/b.wav"], files);
    }

    [Fact]
    public void Split_DoesNotMatchSiblingWithSamePrefix()
    {
        var (folders, files) = FolderListing.Split(["Prov/x/a.wav", "ProvX/y/b.wav"], k => k, "Prov/");

        Assert.Equal(["x"], folders.Select(f => f.Name));
        Assert.Empty(files);
    }

    [Fact]
    public void Page_PagesFoldersThenFiles()
    {
        var folders = Enumerable.Range(1, 3).Select(i => new FolderListing.Folder($"f{i}/", $"f{i}", 1)).ToList();
        var files = Enumerable.Range(1, 4).Select(i => $"file{i}").ToList();

        var (p1Folders, p1Files) = FolderListing.Page(folders, files, page: 1, pageSize: 2);
        var (p2Folders, p2Files) = FolderListing.Page(folders, files, page: 2, pageSize: 2);
        var (p3Folders, p3Files) = FolderListing.Page(folders, files, page: 3, pageSize: 2);
        var (p4Folders, p4Files) = FolderListing.Page(folders, files, page: 4, pageSize: 2);

        Assert.Equal(["f1", "f2"], p1Folders.Select(f => f.Name));
        Assert.Empty(p1Files);
        Assert.Equal(["f3"], p2Folders.Select(f => f.Name));
        Assert.Equal(["file1"], p2Files);
        Assert.Empty(p3Folders);
        Assert.Equal(["file2", "file3"], p3Files);
        Assert.Empty(p4Folders);
        Assert.Equal(["file4"], p4Files);
    }
}
