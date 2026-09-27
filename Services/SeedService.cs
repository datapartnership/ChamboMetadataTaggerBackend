using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Rebuilds the <see cref="Collection"/> / <see cref="CollectionFile"/> tables from catalog
/// storage, which is laid out as <c>Provider/CollectionFolder/...</c>. The media type is
/// inferred from the folder's contents:
/// <list type="bullet">
/// <item>A folder with a <c>*_metadata.jsonld</c> file (anywhere inside it) is an audio or video
/// collection; the jsonld lists its media files and their transcriptions, which may live in
/// subfolders. It is a video collection when any listed file is video.</item>
/// <item>A folder without one is a text collection: its PDF/TXT/MD files are scanned.</item>
/// </list>
/// Every file is enriched with the tags of its ready tagger entry (see <see cref="CatalogTagPublisher"/>).
/// Each run is a full sync: new rows are inserted, changed rows updated, and rows whose
/// content is no longer in storage deleted. IDs are derived from storage paths (or the
/// jsonld identifier), so they stay stable across runs.
/// </summary>
public class SeedService(
    ApplicationDbContext db, IFileStorageService storage, CroissantMetadataParser parser, CatalogTagPublisher publisher)
{
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".txt", ".md" };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".mp3", ".wav", ".ogg", ".opus", ".m4a", ".aac", ".wma", ".aif", ".aiff",
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".mpg", ".mpeg", ".wmv", ".ts",
    };

    private const int MaxWarningExamples = 5;

    private sealed record CollectionFolder(string Provider, string Name, string Prefix, List<StorageFileEntry> Entries)
    {
        /// <summary>Metadata files, closest to the collection root first.</summary>
        public List<StorageFileEntry> MetadataEntries { get; } = [];
    }

    private sealed record BuiltCollection(Collection Collection, List<CollectionFile> Files);

    // ── Public entry point ───────────────────────────────────────────────────

    public async Task<SeedResult> SeedAsync(CancellationToken ct = default)
    {
        var result = new SeedResult();
        foreach (var mediaType in Enum.GetValues<MediaType>())
            result.ByMediaType[mediaType.ToString()] = new SeedCounts();

        var entries = await storage.ListFilesAsync("", ct);
        var folders = GroupByCollection(entries, result.Warnings);
        var taggerLookup = await publisher.LoadReadyAsync(ct: ct);

        var built = new List<BuiltCollection>();
        foreach (var folder in folders)
        {
            var collection = folder.MetadataEntries.Count > 0
                ? await BuildMediaCollectionAsync(folder, result.Warnings, ct)
                : await BuildTextCollectionAsync(folder, taggerLookup, result, ct);
            if (collection is null)
                continue;
            if (collection.Collection.MediaType != MediaType.Text)
                ApplyTaggerData(collection.Files, taggerLookup, result);
            built.Add(collection);
        }

        ReportTaggedFilesNotCataloged(built, taggerLookup, result);
        EnsureUniqueCollectionIds(built, result.Warnings);
        await SyncAsync(built, result, ct);

        var t = result.Totals;
        result.Message =
            $"Seed complete: {t.CollectionsAdded} collection(s) added, {t.CollectionsUpdated} updated, " +
            $"{t.CollectionsDeleted} deleted; {t.FilesAdded} file(s) added, {t.FilesUpdated} updated, " +
            $"{t.FilesDeleted} deleted; {result.Warnings.Count} warning(s).";
        return result;
    }

    // ── Storage layout ───────────────────────────────────────────────────────

    private static List<CollectionFolder> GroupByCollection(IReadOnlyList<StorageFileEntry> entries, List<string> warnings)
    {
        var folders = new Dictionary<string, CollectionFolder>(StringComparer.Ordinal);
        var outsideLayout = new List<string>();

        foreach (var entry in entries)
        {
            var key = CatalogKey.Normalize(entry.Key);
            var segments = key.Split('/');

            // Hidden files and folders (.DS_Store, pipeline checkpoints, ...) are never catalog content.
            if (segments.Any(s => s.StartsWith('.')))
                continue;
            if (segments[..^1].Any(s => s.Contains("checkpoints", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (segments.Length < 3 || segments.Take(2).Any(s => s.Length == 0))
            {
                outsideLayout.Add(key);
                continue;
            }

            var prefix = $"{segments[0]}/{segments[1]}";
            if (!folders.TryGetValue(prefix, out var folder))
                folders[prefix] = folder = new CollectionFolder(segments[0], segments[1], prefix, []);
            folder.Entries.Add(entry with { Key = key });
        }

        if (outsideLayout.Count > 0)
            warnings.Add(
                $"{outsideLayout.Count} file(s) are not inside a Provider/Collection folder and were ignored " +
                $"(e.g. {string.Join(", ", outsideLayout.Take(MaxWarningExamples))}).");

        foreach (var folder in folders.Values)
            folder.MetadataEntries.AddRange(folder.Entries
                .Where(e => e.Key.EndsWith(CroissantMetadataParser.MetadataFileSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Key.Count(c => c == '/'))
                .ThenBy(e => e.Key, StringComparer.Ordinal));

        return [.. folders.Values.OrderBy(f => f.Prefix, StringComparer.Ordinal)];
    }

    // ── Text collections ─────────────────────────────────────────────────────

    private async Task<BuiltCollection?> BuildTextCollectionAsync(
        CollectionFolder folder, Dictionary<string, TaggerData> taggerLookup, SeedResult result, CancellationToken ct)
    {
        var textEntries = folder.Entries
            .Where(e => TextExtensions.Contains(Path.GetExtension(e.Key)))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();

        if (textEntries.Count == 0)
        {
            result.Warnings.Add(folder.Entries.Any(e => MediaExtensions.Contains(Path.GetExtension(e.Key)))
                ? $"Collection '{folder.Prefix}' contains audio/video but no *{CroissantMetadataParser.MetadataFileSuffix} file and was skipped."
                : $"Collection '{folder.Prefix}' has no *{CroissantMetadataParser.MetadataFileSuffix} file and no .pdf, .txt, or .md files and was skipped.");
            return null;
        }

        var collection = new Collection
        {
            Id            = CroissantMetadataParser.CollectionId(folder.Prefix),
            Name          = folder.Name,
            Provider      = folder.Provider,
            MediaType     = MediaType.Text,
            StoragePrefix = folder.Prefix,
            Creator       = folder.Provider,
            Publisher     = folder.Provider,
        };

        var files = new List<CollectionFile>();
        foreach (var entry in textEntries)
        {
            // For local files compute a real SHA-256; for S3/Azure use the ETag to avoid
            // downloading potentially hundreds of megabytes per file.
            var sha256 = entry.ETag is { Length: > 0 } etag
                ? etag
                : await ComputeSha256Async(entry.Key, ct);

            var ext = Path.GetExtension(entry.Key).ToLowerInvariant();

            // PDFs are binary-compressed (~6 bytes per character); plain-text
            // formats (.txt, .md) have a 1:1 size-to-character ratio.
            var charEst = ext == ".pdf" ? (int)(entry.Size / 6) : (int)entry.Size;

            files.Add(new CollectionFile
            {
                Id             = CroissantMetadataParser.FileId(entry.Key),
                CollectionId   = collection.Id,
                ContentUrl     = entry.Key,
                Kind           = FileKind.Text,
                EncodingFormat = ext switch
                {
                    ".txt" => "text/plain",
                    ".md"  => "text/markdown",
                    _      => "application/pdf",
                },
                ContentSize    = entry.Size,
                Sha256         = sha256,
                DatePublished  = entry.LastModified.ToString("O"),
                DateModified   = entry.LastModified.ToString("O"),
                WordCount      = charEst / 5,
                CharCount      = charEst,
                TokenCount     = charEst / 4,
                NumberOfPages  = 1,
            });
        }

        ApplyTaggerData(files, taggerLookup, result);
        collection.Keywords = CatalogTagPublisher.TopValues(files.SelectMany(f => f.Keywords.Select(k => k.Value)));
        collection.Themes   = CatalogTagPublisher.TopValues(files.SelectMany(f => f.Themes.Select(t => t.Value)));

        var dates = textEntries.Select(e => e.LastModified).ToList();
        collection.StartYear     = dates.Min().Year;
        collection.EndYear       = dates.Max().Year;
        collection.DatePublished = dates.Min().ToString("O");
        collection.DateModified  = dates.Max().ToString("O");

        FinalizeCollection(collection, files);
        return new BuiltCollection(collection, files);
    }

    // ── Tagger integration ───────────────────────────────────────────────────

    private static void ApplyTaggerData(List<CollectionFile> files, Dictionary<string, TaggerData> taggerLookup, SeedResult result)
    {
        foreach (var file in files)
        {
            var data = taggerLookup.GetValueOrDefault(file.ContentUrl);
            if (data is not null)
                result.TaggerFilesMatched++;
            CatalogTagPublisher.ApplyTo(file, data);
        }
    }

    private static void ReportTaggedFilesNotCataloged(
        List<BuiltCollection> built, Dictionary<string, TaggerData> taggerLookup, SeedResult result)
    {
        var cataloged = built
            .SelectMany(b => b.Files.Select(f => f.ContentUrl))
            .ToHashSet(StringComparer.Ordinal);
        var missed = taggerLookup.Keys
            .Where(k => !cataloged.Contains(k))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (missed.Count == 0)
            return;

        result.TaggedFilesNotCataloged = missed.Count;
        result.TaggedFilesNotCatalogedExamples = [.. missed.Take(MaxWarningExamples)];
        result.Warnings.Add(
            $"{missed.Count} submitted or approved tagged file(s) match no catalog file, so their tags are not in the catalog " +
            $"(e.g. {string.Join(", ", result.TaggedFilesNotCatalogedExamples)}). Check they are .pdf/.txt/.md files or listed " +
            "media inside a Provider/Collection folder, and that they still exist in storage.");
    }

    // ── Audio / video collections ────────────────────────────────────────────

    private async Task<BuiltCollection?> BuildMediaCollectionAsync(
        CollectionFolder folder, List<string> warnings, CancellationToken ct)
    {
        var metadataEntries = folder.MetadataEntries;
        var metadataKey = metadataEntries[0].Key;
        if (metadataEntries.Count > 1)
            warnings.Add($"Collection '{folder.Prefix}' has {metadataEntries.Count} metadata files; using '{metadataKey}'.");

        Collection collection;
        List<CollectionFile> files;
        try
        {
            string json;
            await using (var stream = await storage.OpenReadAsync(metadataKey, ct))
            using (var reader = new StreamReader(stream))
                json = await reader.ReadToEndAsync(ct);

            (collection, files) = parser.Parse(json, folder.Prefix, folder.Provider, MediaType.Audio);
            // Re-parse so files without a recognizable format default to video, like the rest of the collection.
            if (files.Any(f => f.Kind == FileKind.Video))
                (collection, files) = parser.Parse(json, folder.Prefix, folder.Provider, MediaType.Video);
        }
        catch (JsonException ex)
        {
            warnings.Add($"Metadata file '{metadataKey}' is not valid JSON and its collection was skipped: {ex.Message}");
            return null;
        }

        var inStorage = folder.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var relocated = RelocateByFileName(files, folder.Entries, inStorage);
        if (relocated > 0)
            warnings.Add(
                $"{relocated} file(s) listed in '{metadataKey}' were not at their listed path " +
                "and were matched to a storage file by name.");

        files = [.. files.GroupBy(f => f.Id).Select(g => g.First())];

        var missing = 0;
        foreach (var file in files)
        {
            if (!inStorage.TryGetValue(file.ContentUrl, out var entry))
            {
                missing++;
                continue;
            }
            file.ContentSize  ??= entry.Size;
            file.DateModified ??= entry.LastModified.ToString("O");
        }
        if (missing > 0)
            warnings.Add($"{missing} file(s) listed in '{metadataKey}' were not found in storage.");

        var listed = files.Select(f => f.ContentUrl).ToHashSet(StringComparer.Ordinal);
        var unlisted = folder.Entries.Count(e => MediaExtensions.Contains(Path.GetExtension(e.Key)) && !listed.Contains(e.Key));
        if (unlisted > 0)
            warnings.Add($"{unlisted} media file(s) in '{folder.Prefix}' are not listed in '{metadataKey}' and were not cataloged.");

        var documents = folder.Entries.Count(e =>
            Path.GetExtension(e.Key).ToLowerInvariant() is ".pdf" or ".md" && !listed.Contains(e.Key));
        if (documents > 0)
            warnings.Add(
                $"{documents} .pdf/.md file(s) in '{folder.Prefix}' were not cataloged: a folder with a metadata file " +
                "is an audio/video collection.");

        FinalizeCollection(collection, files);
        return new BuiltCollection(collection, files);
    }

    /// <summary>
    /// Points files whose resolved key is not in storage at the collection's only storage file
    /// with the same name, then re-pairs transcriptions since the IDs changed.
    /// </summary>
    /// <returns>Number of files relocated.</returns>
    private static int RelocateByFileName(
        List<CollectionFile> files, List<StorageFileEntry> entries, Dictionary<string, StorageFileEntry> inStorage)
    {
        var claimed = files.Select(f => f.ContentUrl).Where(inStorage.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var byName = entries
            .Where(e => !claimed.Contains(e.Key))
            .GroupBy(e => Path.GetFileName(e.Key), StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Key, StringComparer.Ordinal);

        var relocated = 0;
        foreach (var file in files.Where(f => !inStorage.ContainsKey(f.ContentUrl)))
        {
            if (!byName.Remove(Path.GetFileName(file.ContentUrl), out var key))
                continue;
            file.ContentUrl = key;
            file.Id         = CroissantMetadataParser.FileId(key);
            relocated++;
        }

        if (relocated > 0)
        {
            foreach (var file in files)
                file.TranscriptionFileId = null;
            CroissantMetadataParser.PairTranscriptions(files);
        }
        return relocated;
    }

    private static void FinalizeCollection(Collection collection, List<CollectionFile> files)
    {
        var primary = files.Where(f => f.Kind != FileKind.Transcription).ToList();
        var durations = primary.Where(f => f.Duration.HasValue).Select(f => f.Duration!.Value).ToList();

        collection.FileCount            = primary.Count;
        collection.TotalDurationSeconds = durations.Count > 0 ? durations.Sum() : null;
        collection.Formats              = primary.Count > 0
            ? string.Join(",", primary.Select(f => f.EncodingFormat).Distinct(StringComparer.OrdinalIgnoreCase).Order())
            : null;
    }

    /// <summary>Two folders reusing one jsonld <c>sc:identifier</c> would collide; later ones fall back to a path-based ID.</summary>
    private static void EnsureUniqueCollectionIds(List<BuiltCollection> built, List<string> warnings)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (collection, files) in built)
        {
            if (used.Add(collection.Id))
                continue;

            var fallback = CroissantMetadataParser.CollectionId(collection.StoragePrefix);
            warnings.Add(
                $"Collection '{collection.StoragePrefix}' reuses identifier '{collection.Id}'; it was assigned '{fallback}' instead.");
            collection.Id = fallback;
            used.Add(fallback);
            foreach (var file in files)
                file.CollectionId = fallback;
        }
    }

    // ── Database sync ────────────────────────────────────────────────────────

    private async Task SyncAsync(List<BuiltCollection> built, SeedResult result, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var existingCollections = await db.Collections.ToDictionaryAsync(c => c.Id, ct);
        var existingFiles = await db.CollectionFiles
            .Include(f => f.Keywords)
            .Include(f => f.Themes)
            .AsSplitQuery()
            .ToDictionaryAsync(f => f.Id, ct);
        var previousMediaType = existingCollections.Values.ToDictionary(c => c.Id, c => c.MediaType);

        var seenCollections = new HashSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (collection, files) in built)
        {
            var counts = result.ByMediaType[collection.MediaType.ToString()];
            seenCollections.Add(collection.Id);

            if (existingCollections.TryGetValue(collection.Id, out var current))
            {
                collection.SeededAtUtc = current.SeededAtUtc;
                var entry = db.Entry(current);
                entry.CurrentValues.SetValues(collection);
                if (entry.State == EntityState.Modified)
                    counts.CollectionsUpdated++;
                current.SeededAtUtc = now;
            }
            else
            {
                collection.SeededAtUtc = now;
                db.Collections.Add(collection);
                counts.CollectionsAdded++;
            }

            foreach (var file in files)
            {
                seenFiles.Add(file.Id);

                if (!existingFiles.TryGetValue(file.Id, out var currentFile))
                {
                    db.CollectionFiles.Add(file);
                    counts.FilesAdded++;
                    continue;
                }

                var entry = db.Entry(currentFile);
                entry.CurrentValues.SetValues(file);
                var changed = entry.State == EntityState.Modified;
                changed |= CatalogTagPublisher.SyncTags(db, currentFile.Keywords, file.Keywords.Select(k => k.Value), k => k.Value, v => new Keyword { Value = v });
                changed |= CatalogTagPublisher.SyncTags(db, currentFile.Themes, file.Themes.Select(t => t.Value), t => t.Value, v => new Theme { Value = v });
                if (changed)
                    counts.FilesUpdated++;
            }
        }

        // Pick up reassigned CollectionIds before deleting, so removing an old collection
        // does not cascade to files that moved to a new one.
        db.ChangeTracker.DetectChanges();

        foreach (var (id, file) in existingFiles)
        {
            if (seenFiles.Contains(id))
                continue;
            db.CollectionFiles.Remove(file);
            result.ByMediaType[previousMediaType[file.CollectionId].ToString()].FilesDeleted++;
        }

        foreach (var (id, collection) in existingCollections)
        {
            if (seenCollections.Contains(id))
                continue;
            db.Collections.Remove(collection);
            result.ByMediaType[previousMediaType[id].ToString()].CollectionsDeleted++;
        }

        await db.SaveChangesAsync(ct);

        foreach (var counts in result.ByMediaType.Values)
        {
            result.Totals.CollectionsAdded   += counts.CollectionsAdded;
            result.Totals.CollectionsUpdated += counts.CollectionsUpdated;
            result.Totals.CollectionsDeleted += counts.CollectionsDeleted;
            result.Totals.FilesAdded         += counts.FilesAdded;
            result.Totals.FilesUpdated       += counts.FilesUpdated;
            result.Totals.FilesDeleted       += counts.FilesDeleted;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> ComputeSha256Async(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(key, ct);
            var hash = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            // Fall back to a deterministic hash of the key if the file cannot be opened
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        }
    }
}
