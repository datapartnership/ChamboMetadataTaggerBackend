namespace MetadataTagging.Services;

/// <summary>
/// Turns a flat list of storage keys into the immediate children of one folder
/// (sub-folders and direct files), so listings can be browsed and paged per folder
/// instead of per file.
/// </summary>
public static class FolderListing
{
    public sealed record Folder(string Path, string Name, int FileCount);

    /// <summary><c>""</c> for the root, otherwise the normalized folder path with a trailing slash.</summary>
    public static string NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "";
        var normalized = CatalogKey.Normalize(folder).TrimEnd('/');
        return normalized.Length == 0 ? "" : normalized + "/";
    }

    /// <summary>
    /// Splits <paramref name="items"/> under <paramref name="folder"/> into sub-folders (sorted by name,
    /// with the number of files beneath each) and files directly in the folder (in input order).
    /// Items outside the folder are ignored.
    /// </summary>
    public static (List<Folder> Folders, List<T> Files) Split<T>(IEnumerable<T> items, Func<T, string> key, string folder)
    {
        var folderCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new List<T>();

        foreach (var item in items)
        {
            var itemKey = CatalogKey.Normalize(key(item));
            if (!itemKey.StartsWith(folder, StringComparison.Ordinal))
                continue;

            var relative = itemKey[folder.Length..];
            if (relative.Length == 0)
                continue;

            var slash = relative.IndexOf('/');
            if (slash < 0)
            {
                files.Add(item);
                continue;
            }

            var name = relative[..slash];
            folderCounts[name] = folderCounts.GetValueOrDefault(name) + 1;
        }

        var folders = folderCounts
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new Folder(folder + kv.Key + "/", kv.Key, kv.Value))
            .ToList();

        return (folders, files);
    }

    /// <summary>
    /// Pages over folders followed by files, returning the slice of each that falls on <paramref name="page"/>.
    /// </summary>
    public static (List<Folder> Folders, List<T> Files) Page<T>(List<Folder> folders, List<T> files, int page, int pageSize)
    {
        var skip = (page - 1) * pageSize;
        var pageFolders = folders.Skip(skip).Take(pageSize).ToList();
        var fileSkip = Math.Max(0, skip - folders.Count);
        var pageFiles = files.Skip(fileSkip).Take(pageSize - pageFolders.Count).ToList();
        return (pageFolders, pageFiles);
    }
}
