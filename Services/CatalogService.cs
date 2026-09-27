using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Provides publicly-accessible collection catalog queries used by the catalog browser UI.
/// All methods return plain DTO objects; no authentication is required by callers.
/// </summary>
public class CatalogService(ApplicationDbContext db)
{
    public async Task<CatalogPagedResult<CollectionCatalogListItem>> ListCollectionsAsync(
        CollectionCatalogQueryParams p, CancellationToken ct = default)
    {
        var query = db.Collections.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Q))
        {
            var q = p.Q.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(q) ||
                c.Provider.ToLower().Contains(q) ||
                (c.Description != null && c.Description.ToLower().Contains(q)));
        }

        if (TryParseEnum<MediaType>(p.MediaType, out var mediaType))
            query = query.Where(c => c.MediaType == mediaType);

        if (!string.IsNullOrWhiteSpace(p.Language))
        {
            var lang = p.Language.ToLower();
            query = query.Where(c =>
                (c.LanguageName != null && c.LanguageName.ToLower().Contains(lang)) ||
                (c.LanguageCode != null && c.LanguageCode.ToLower().Contains(lang)));
        }

        if (!string.IsNullOrWhiteSpace(p.Provider))
        {
            var prov = p.Provider.ToLower();
            query = query.Where(c => c.Provider.ToLower().Contains(prov));
        }

        if (!string.IsNullOrWhiteSpace(p.Theme))
        {
            var theme = p.Theme.ToLower();
            query = query.Where(c => c.Themes != null && c.Themes.ToLower().Contains(theme));
        }

        if (p.Year.HasValue)
        {
            var year = p.Year.Value;
            query = query.Where(c =>
                (c.StartYear == null || c.StartYear <= year) &&
                (c.EndYear == null || c.EndYear >= year));
        }

        var totalCount = await query.CountAsync(ct);
        var pageSize = Math.Clamp(p.PageSize, 1, 50);
        var page = Math.Max(p.Page, 1);

        var collections = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new CatalogPagedResult<CollectionCatalogListItem>
        {
            page       = page,
            pageSize   = pageSize,
            totalCount = totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            results    = [.. collections.Select(c => Fill(new CollectionCatalogListItem(), c))],
        };
    }

    public async Task<CollectionCatalogDetail?> GetCollectionAsync(string id, CancellationToken ct = default)
    {
        var c = await db.Collections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        var detail = Fill(new CollectionCatalogDetail(), c);
        detail.description        = c.Description;
        detail.keywords           = Split(c.Keywords);
        detail.storagePath        = c.StoragePrefix;
        detail.temporalCoverage   = c.TemporalCoverage;
        detail.datePublished      = c.DatePublished;
        detail.dateModified       = c.DateModified;
        detail.license            = c.License;
        detail.creator            = c.Creator;
        detail.publisher          = c.Publisher;
        detail.sourceOrganization = c.SourceOrganization;
        detail.version            = c.Version;
        detail.piiScreening       = c.PiiScreening;
        detail.sensitiveContent   = c.SensitiveContent;
        detail.seededAtUtc        = c.SeededAtUtc;
        detail.transcriptionCount = await db.CollectionFiles
            .CountAsync(f => f.CollectionId == id && f.Kind == FileKind.Transcription, ct);
        return detail;
    }

    /// <returns><c>null</c> when the collection does not exist.</returns>
    public async Task<CatalogPagedResult<CollectionFileItem>?> ListCollectionFilesAsync(
        string id, CollectionFileQueryParams p, CancellationToken ct = default)
    {
        if (!await db.Collections.AnyAsync(c => c.Id == id, ct))
            return null;

        var query = db.CollectionFiles.AsNoTracking().Where(f => f.CollectionId == id);

        if (!string.IsNullOrWhiteSpace(p.Q))
        {
            var q = p.Q.ToLower();
            query = query.Where(f =>
                f.Name.ToLower().Contains(q) ||
                (f.Author != null && f.Author.ToLower().Contains(q)));
        }

        if (TryParseEnum<FileKind>(p.Kind, out var kind))
            query = query.Where(f => f.Kind == kind);

        var totalCount = await query.CountAsync(ct);
        var pageSize = Math.Clamp(p.PageSize, 1, 100);
        var page = Math.Max(p.Page, 1);

        var files = await query
            .Include(f => f.Keywords)
            .Include(f => f.Themes)
            .OrderBy(f => f.Name)
            .ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new CatalogPagedResult<CollectionFileItem>
        {
            page       = page,
            pageSize   = pageSize,
            totalCount = totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            results    = [.. files.Select(f => new CollectionFileItem
            {
                id                  = f.Id,
                name                = f.Name,
                kind                = f.Kind.ToString(),
                encodingFormat      = f.EncodingFormat,
                contentSize         = f.ContentSize,
                dateModified        = f.DateModified,
                author              = f.Author,
                datePublished       = f.DatePublished,
                wordCount           = f.WordCount,
                numberOfPages       = f.NumberOfPages,
                duration            = f.Duration,
                sampleRate          = f.SampleRate,
                channels            = f.Channels,
                bitrate             = f.Bitrate,
                transcriptionFileId = f.TranscriptionFileId,
                keywords            = [.. f.Keywords.Select(k => k.Value).Order()],
                themes              = [.. f.Themes.Select(t => t.Value).Order()],
            })],
        };
    }

    public async Task<CollectionCatalogFacetsResponse> GetFacetsAsync(CancellationToken ct = default)
    {
        var collections = await db.Collections
            .AsNoTracking()
            .Select(c => new { c.MediaType, c.Provider, c.LanguageName, c.LanguageCode, c.Themes, c.StartYear, c.EndYear })
            .ToListAsync(ct);

        static List<FacetItem> Count(IEnumerable<string> values) =>
            [.. values
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FacetItem { value = g.Key, count = g.Count() })
                .OrderByDescending(f => f.count)
                .ThenBy(f => f.value)];

        var years = collections
            .Where(c => c.StartYear.HasValue || c.EndYear.HasValue)
            .SelectMany(c =>
            {
                var start = c.StartYear ?? c.EndYear!.Value;
                var end = Math.Max(start, c.EndYear ?? start);
                return Enumerable.Range(start, end - start + 1);
            })
            .GroupBy(y => y)
            .Select(g => new FacetItem { value = g.Key.ToString(), count = g.Count() })
            .OrderByDescending(f => f.value)
            .ToList();

        return new CollectionCatalogFacetsResponse
        {
            totalCollections = collections.Count,
            mediaTypes = Count(collections.Select(c => c.MediaType.ToString())),
            providers  = Count(collections.Select(c => c.Provider)),
            languages  = Count(collections
                .Select(c => c.LanguageName ?? c.LanguageCode)
                .OfType<string>()),
            themes     = [.. Count(collections.SelectMany(c => Split(c.Themes))).Take(30)],
            years      = years,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T Fill<T>(T item, Collection c) where T : CollectionCatalogListItem
    {
        item.id           = c.Id;
        item.name         = c.Name;
        item.provider     = c.Provider;
        item.mediaType    = c.MediaType.ToString();
        item.language     = c.LanguageName ?? c.LanguageCode;
        item.languageCode = c.LanguageCode;
        item.formats      = Split(c.Formats);
        item.hours        = c.TotalDurationSeconds is { } seconds ? Math.Round(seconds / 3600, 2) : null;
        item.items        = c.FileCount;
        item.startYear    = c.StartYear;
        item.endYear      = c.EndYear;
        item.themes       = Split(c.Themes);
        return item;
    }

    private static List<string> Split(string? values) =>
        values is null
            ? []
            : [.. values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    internal static bool TryParseEnum<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out result)
        && Enum.IsDefined(result)
        && !int.TryParse(value, out _);
}
