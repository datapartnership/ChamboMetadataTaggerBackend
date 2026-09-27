namespace MetadataTagging.Services;

/// <summary>
/// Maps catalog keys (<c>Provider/Collection/...</c>) onto blob names under an optional
/// container prefix such as <c>markdown/CATALOGPROD</c>.
/// </summary>
internal static class AzureBlobPaths
{
    /// <summary>Forward slashes, no leading or trailing slash. Empty when unset.</summary>
    public static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return "";
        return CatalogKey.Normalize(prefix).TrimEnd('/');
    }

    public static string ToBlobName(string prefix, string key)
    {
        var logical = CatalogKey.Normalize(key);
        return prefix.Length == 0 ? logical : $"{prefix}/{logical}";
    }

    /// <summary>
    /// Catalog key for a blob name, or <c>null</c> when the blob is the prefix folder itself
    /// or lies outside the prefix.
    /// </summary>
    public static string? ToLogicalKey(string prefix, string blobName)
    {
        var name = CatalogKey.Normalize(blobName);
        if (prefix.Length == 0)
            return name.Length == 0 ? null : name;

        var root = prefix + "/";
        if (!name.StartsWith(root, StringComparison.Ordinal))
            return null;

        var logical = name[root.Length..];
        return logical.Length == 0 ? null : logical;
    }

    /// <summary>Blob listing prefix, or <c>null</c> to list the whole container.</summary>
    public static string? ToListPrefix(string prefix, string? callerPrefix)
    {
        var caller = string.IsNullOrEmpty(callerPrefix) ? "" : CatalogKey.Normalize(callerPrefix);
        if (prefix.Length == 0)
            return caller.Length == 0 ? null : caller;

        // Trailing slash so "markdown/CATALOG" does not also match "markdown/CATALOGPROD".
        return caller.Length == 0 ? prefix + "/" : prefix + "/" + caller.TrimStart('/');
    }
}
