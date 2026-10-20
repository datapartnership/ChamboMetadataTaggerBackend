using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>Catalog metadata taken from a tagger's <see cref="FileTag"/>s.</summary>
public sealed record TaggerData(IReadOnlyList<string> Themes, IReadOnlyList<string> Keywords, string? Author, string? Title);

/// <summary>
/// Copies tagger tags onto the catalog: keywords, themes and author on every
/// <see cref="CollectionFile"/>, plus the title as the name of text files. Only files whose
/// tagging is in one of the <see cref="ReadyStatuses"/> contribute; any other file gets the
/// values storage alone provides (no tags, file name as title).
/// Used by the seed for freshly built rows, and by the tagging workflow to update existing
/// rows as soon as a file's tags or status change.
/// </summary>
public class CatalogTagPublisher(ApplicationDbContext db)
{
    public static readonly FileTaggingStatus[] ReadyStatuses =
        [FileTaggingStatus.SubmittedToSupervisor, FileTaggingStatus.ApprovedBySupervisor];

    private const int MaxCollectionTags = 20;
    private const int QueryChunkSize = 500;

    /// <summary>Ready tagger data keyed by storage key, for <paramref name="keys"/> or for every ready file when <c>null</c>.</summary>
    public async Task<Dictionary<string, TaggerData>> LoadReadyAsync(
        IReadOnlyCollection<string>? keys = null, CancellationToken ct = default)
    {
        var result = new Dictionary<string, TaggerData>(StringComparer.Ordinal);
        var chunks = keys is null ? [null] : keys.Chunk(QueryChunkSize).Select(c => (string[]?)c);
        foreach (var chunk in chunks)
        {
            var query = db.FileMetadata
                .AsNoTracking()
                .Include(m => m.Tags)
                .Where(m => m.BlobName != null && ReadyStatuses.Contains(m.Status));
            if (chunk is not null)
                query = query.Where(m => chunk.Contains(m.BlobName!));

            foreach (var m in await query.ToListAsync(ct))
                result.TryAdd(m.BlobName!, Extract(m.Tags));
        }
        return result;
    }

    public static TaggerData Extract(IEnumerable<FileTag> tags)
    {
        IReadOnlyList<string> themes = [];
        IReadOnlyList<string> keywords = [];
        string? author = null;
        string? title = null;

        foreach (var tag in tags)
        {
            switch (tag.TagKey)
            {
                case "Theme":
                    // "Health > Clinics | Education" → top-level themes only.
                    themes = tag.TagValue
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(t => t.Contains('>') ? t[..t.IndexOf('>')].Trim() : t)
                        .Where(t => t.Length > 0)
                        .Distinct()
                        .ToList();
                    break;
                case "Keywords":
                    keywords = tag.TagValue
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct()
                        .ToList();
                    break;
                case "Author":
                    author = NullIfBlank(tag.TagValue);
                    break;
                case "Title":
                    title = NullIfBlank(tag.TagValue);
                    break;
            }
        }

        return new TaggerData(themes, keywords, author, title);
    }

    /// <summary>Sets the tag-derived values of a new, untracked catalog file.</summary>
    public static void ApplyTo(CollectionFile file, TaggerData? data)
    {
        SetScalars(file, data);
        file.Keywords = [.. (data?.Keywords ?? []).Select(v => new Keyword { Value = v })];
        file.Themes   = [.. (data?.Themes ?? []).Select(v => new Theme { Value = v })];
    }

    /// <summary>
    /// Re-applies tagger data to the catalog files stored at <paramref name="keys"/> and
    /// refreshes the keywords/themes of their text collections. Keys without a catalog file are ignored.
    /// </summary>
    public async Task ApplyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var keyList = keys.Select(CatalogKey.Normalize).Distinct(StringComparer.Ordinal).ToList();
        if (keyList.Count == 0)
            return;

        var data = await LoadReadyAsync(keyList, ct);
        var textCollections = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;

        foreach (var chunk in keyList.Chunk(QueryChunkSize))
        {
            var files = await db.CollectionFiles
                .Include(f => f.Collection)
                .Include(f => f.Keywords)
                .Include(f => f.Themes)
                .Where(f => chunk.Contains(f.ContentUrl))
                .AsSplitQuery()
                .ToListAsync(ct);

            foreach (var file in files)
            {
                var tagData = data.GetValueOrDefault(file.ContentUrl);
                SetScalars(file, tagData);
                changed |= db.Entry(file).State == EntityState.Modified;
                changed |= SyncTags(db, file.Keywords, tagData?.Keywords ?? [], k => k.Value, v => new Keyword { Value = v });
                changed |= SyncTags(db, file.Themes, tagData?.Themes ?? [], t => t.Value, v => new Theme { Value = v });
                if (file.Collection?.MediaType == MediaType.Text)
                    textCollections.Add(file.CollectionId);
            }
        }

        if (!changed)
            return;
        await db.SaveChangesAsync(ct);

        // Audio/video collections keep the keywords and themes from their jsonld.
        foreach (var id in textCollections)
        {
            var collection = await db.Collections.FindAsync([id], ct);
            if (collection is null)
                continue;
            collection.Keywords = TopValues(await db.Keywords.Where(k => k.File!.CollectionId == id).Select(k => k.Value).ToListAsync(ct));
            collection.Themes   = TopValues(await db.Themes.Where(t => t.File!.CollectionId == id).Select(t => t.Value).ToListAsync(ct));
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The most frequent values as a comma-separated list, or <c>null</c> when there are none.</summary>
    public static string? TopValues(IEnumerable<string> values)
    {
        var top = values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCollectionTags)
            .Select(g => g.Key.Replace(",", " "))
            .ToList();
        return top.Count == 0 ? null : string.Join(",", top);
    }

    /// <returns><c>true</c> when the stored tag values differed and were replaced.</returns>
    public static bool SyncTags<T>(
        ApplicationDbContext db, ICollection<T> current, IEnumerable<string> desired, Func<T, string> value, Func<string, T> create)
        where T : class
    {
        var wanted = desired.Order(StringComparer.Ordinal).ToList();
        if (current.Select(value).Order(StringComparer.Ordinal).SequenceEqual(wanted))
            return false;

        foreach (var tag in current.ToList())
        {
            current.Remove(tag);
            db.Remove(tag);
        }
        foreach (var v in wanted)
            current.Add(create(v));
        return true;
    }

    private static void SetScalars(CollectionFile file, TaggerData? data)
    {
        file.Author = data?.Author;
        if (file.Kind == FileKind.Text)
            file.Name = data?.Title ?? Path.GetFileName(file.ContentUrl);
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
