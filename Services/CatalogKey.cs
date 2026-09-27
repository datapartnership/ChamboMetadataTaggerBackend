namespace MetadataTagging.Services;

/// <summary>
/// Storage keys shared by the tagging workflow (<c>FileMetadata.BlobName</c>) and the catalog
/// (<c>CollectionFile.ContentUrl</c>). Both are stored in normalized form so they can be
/// compared directly.
/// </summary>
public static class CatalogKey
{
    /// <summary>Forward slashes, no leading <c>./</c> or <c>/</c>.</summary>
    public static string Normalize(string key)
    {
        key = key.Replace('\\', '/');
        if (key.StartsWith("./", StringComparison.Ordinal))
            key = key[2..];
        return key.TrimStart('/');
    }

    /// <summary>
    /// <c>true</c> for a file inside a <c>Provider/Collection/</c> folder that is not hidden and
    /// is not a collection's <c>*_metadata.jsonld</c>, i.e. content the seed can catalog.
    /// </summary>
    public static bool IsCollectionContent(string key)
    {
        var segments = Normalize(key).Split('/');
        return segments.Length >= 3
               && segments.All(s => s.Length > 0 && !s.StartsWith('.'))
               && !segments[^1].EndsWith(CroissantMetadataParser.MetadataFileSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
