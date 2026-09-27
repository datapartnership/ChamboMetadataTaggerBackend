using MetadataTagging.Services;

namespace MetadataTagging.Tests;

public class AzureBlobPathsTests
{
    private const string Prefix = "markdown/CATALOGPROD";

    [Fact]
    public void NormalizePrefix_TrimsSlashesAndDotSlash()
    {
        Assert.Equal(Prefix, AzureBlobPaths.NormalizePrefix($"./{Prefix}/"));
        Assert.Equal("", AzureBlobPaths.NormalizePrefix("  "));
    }

    [Fact]
    public void ToBlobName_PrependsPrefix()
    {
        Assert.Equal($"{Prefix}/Radio Maria/BIBLE QUIZ/ep01.flac",
            AzureBlobPaths.ToBlobName(Prefix, "Radio Maria/BIBLE QUIZ/ep01.flac"));
    }

    [Fact]
    public void ToBlobName_WithoutPrefix_KeepsCatalogKey()
    {
        Assert.Equal("Radio Maria/ep01.flac", AzureBlobPaths.ToBlobName("", "./Radio Maria/ep01.flac"));
    }

    [Fact]
    public void ToLogicalKey_StripsPrefix()
    {
        Assert.Equal("Radio Maria/BIBLE QUIZ/ep01.flac",
            AzureBlobPaths.ToLogicalKey(Prefix, $"{Prefix}/Radio Maria/BIBLE QUIZ/ep01.flac"));
    }

    [Fact]
    public void ToLogicalKey_IgnoresThePrefixFolderAndBlobsOutsideIt()
    {
        Assert.Null(AzureBlobPaths.ToLogicalKey(Prefix, $"{Prefix}/"));
        Assert.Null(AzureBlobPaths.ToLogicalKey(Prefix, "markdown/OTHER/Radio Maria/ep01.flac"));
    }

    [Fact]
    public void ToListPrefix_EndsWithSlashSoAShorterNameDoesNotMatch()
    {
        Assert.Equal($"{Prefix}/", AzureBlobPaths.ToListPrefix(Prefix, ""));
        Assert.Equal($"{Prefix}/Radio Maria/", AzureBlobPaths.ToListPrefix(Prefix, "Radio Maria/"));
        Assert.Null(AzureBlobPaths.ToListPrefix("", ""));
        Assert.Equal("Radio Maria/", AzureBlobPaths.ToListPrefix("", "Radio Maria/"));
    }
}
