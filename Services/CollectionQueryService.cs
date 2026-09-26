using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>Result of a Croissant query: sampled files grouped by collection, plus related data.</summary>
public record CollectionQueryResult(
    List<(Collection Collection, CollectionFile[] Files)> Groups,
    IReadOnlyDictionary<string, CollectionFile> Transcriptions,
    Dictionary<string, List<FileTag>> TaggerTags,
    int TotalMatched,
    int AppliedLimit);

/// <summary>
/// Queries primary (non-transcription) collection files with optional filters.
/// Filtered queries are randomly sampled to their limit; unfiltered queries are randomly
/// sampled to 80%. Transcriptions are not sampled on their own: every sampled media
/// file's transcription is returned alongside it.
/// </summary>
public class CollectionQueryService(ApplicationDbContext db)
{
    public async Task<CollectionQueryResult> QueryAsync(CollectionQueryParams p, CancellationToken ct = default)
    {
        var hasFilters =
            !string.IsNullOrWhiteSpace(p.MediaType) ||
            !string.IsNullOrWhiteSpace(p.CollectionId) ||
            !string.IsNullOrWhiteSpace(p.Provider) ||
            !string.IsNullOrWhiteSpace(p.Language) ||
            !string.IsNullOrWhiteSpace(p.Keyword) ||
            !string.IsNullOrWhiteSpace(p.Theme) ||
            !string.IsNullOrWhiteSpace(p.Author) ||
            p.Year.HasValue;

        var query = db.CollectionFiles
            .Include(f => f.Collection)
            .Include(f => f.Keywords)
            .Include(f => f.Themes)
            .Where(f => f.Kind != FileKind.Transcription)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.MediaType))
        {
            if (!CatalogService.TryParseEnum<MediaType>(p.MediaType, out var mediaType))
                return new CollectionQueryResult([], new Dictionary<string, CollectionFile>(), [], 0, p.Limit ?? 30);
            query = query.Where(f => f.Collection!.MediaType == mediaType);
        }

        if (!string.IsNullOrWhiteSpace(p.CollectionId))
            query = query.Where(f => f.CollectionId == p.CollectionId);

        if (!string.IsNullOrWhiteSpace(p.Provider))
        {
            var prov = p.Provider.ToLower();
            query = query.Where(f => f.Collection!.Provider.ToLower().Contains(prov));
        }

        if (!string.IsNullOrWhiteSpace(p.Language))
        {
            var lang = p.Language.ToLower();
            query = query.Where(f =>
                (f.Collection!.LanguageCode != null && f.Collection.LanguageCode.ToLower().Contains(lang)) ||
                (f.Collection!.LanguageName != null && f.Collection.LanguageName.ToLower().Contains(lang)));
        }

        if (!string.IsNullOrWhiteSpace(p.Keyword))
        {
            var kw = p.Keyword.ToLower();
            query = query.Where(f =>
                f.Keywords.Any(k => k.Value.ToLower().Contains(kw)) ||
                (f.Collection!.Keywords != null && f.Collection.Keywords.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(p.Theme))
        {
            var th = p.Theme.ToLower();
            query = query.Where(f =>
                f.Themes.Any(t => t.Value.ToLower().Contains(th)) ||
                (f.Collection!.Themes != null && f.Collection.Themes.ToLower().Contains(th)));
        }

        if (!string.IsNullOrWhiteSpace(p.Author))
        {
            var auth = p.Author.ToLower();
            query = query.Where(f => f.Author != null && f.Author.ToLower().Contains(auth));
        }

        if (p.Year.HasValue)
        {
            var year = p.Year.Value;
            var yearText = year.ToString();
            query = query.Where(f =>
                f.Kind == FileKind.Text
                    ? f.DatePublished != null && f.DatePublished.StartsWith(yearText)
                    : (f.Collection!.StartYear == null || f.Collection.StartYear <= year) &&
                      (f.Collection!.EndYear == null || f.Collection.EndYear >= year) &&
                      (f.Collection!.StartYear != null || f.Collection.EndYear != null));
        }

        // Fetch all matches (needed for random sampling)
        var all = await query.ToListAsync(ct);
        var total = all.Count;
        var limit = p.Limit ?? (hasFilters ? 30 : (int)Math.Ceiling(total * 0.8));

        // Shuffle selected metadata before returning it; this does not affect file content.
        var sampled = total <= limit
            ? all
            : all.OrderBy(_ => Guid.NewGuid()).Take(limit).ToList();

        var groups = sampled
            .GroupBy(f => f.Collection!)
            .Select(g => (Collection: g.Key, Files: g.ToArray()))
            .ToList();

        var transcriptions = await LoadTranscriptionsAsync(db, sampled, ct);
        var taggerTags = await LoadTaggerTagsAsync(sampled.Where(f => f.Kind == FileKind.Text), ct);

        return new CollectionQueryResult(groups, transcriptions, taggerTags, total, limit);
    }

    /// <summary>Loads the transcription records paired with the given media files, keyed by Id.</summary>
    public static async Task<IReadOnlyDictionary<string, CollectionFile>> LoadTranscriptionsAsync(
        ApplicationDbContext db, IEnumerable<CollectionFile> files, CancellationToken ct = default)
    {
        var ids = files
            .Select(f => f.TranscriptionFileId)
            .OfType<string>()
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<string, CollectionFile>();

        return await db.CollectionFiles
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);
    }

    /// <returns>Ready tagger tags keyed by <see cref="CollectionFile.ContentUrl"/>.</returns>
    private async Task<Dictionary<string, List<FileTag>>> LoadTaggerTagsAsync(
        IEnumerable<CollectionFile> files, CancellationToken ct)
    {
        var keys = files.Select(f => f.ContentUrl).ToList();
        if (keys.Count == 0)
            return [];

        var metadata = await db.FileMetadata
            .Include(m => m.Tags)
            .Where(m => m.BlobName != null
                        && keys.Contains(m.BlobName)
                        && CatalogTagPublisher.ReadyStatuses.Contains(m.Status))
            .ToListAsync(ct);

        var result = new Dictionary<string, List<FileTag>>(StringComparer.Ordinal);
        foreach (var m in metadata)
            result.TryAdd(m.BlobName!, [.. m.Tags]);
        return result;
    }
}
