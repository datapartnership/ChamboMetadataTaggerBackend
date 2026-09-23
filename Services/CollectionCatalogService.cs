using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.DTOs;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

/// <summary>
/// Rebuilds the "Collections" catalog by parsing an Excel workbook stored in blob storage,
/// and provides query helpers for the read API. The workbook is expected to have columns:
/// Name, Provider, Language, Type, Format, Hours, Items, Description, Start Year, End Year,
/// Themes, Collected, Processing, Availability, Comments, Tagged for Text.
/// </summary>
public class CollectionCatalogService(
    ApplicationDbContext db,
    IFileStorageService mainStorage,
    [FromKeyedServices("sound")] IFileStorageService soundStorage,
    [FromKeyedServices("collections")] IFileStorageService storage,
    IOptions<CollectionCatalogOptions> options,
    ILogger<CollectionCatalogService> logger)
{
    // ── Sync ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the configured Excel workbook and fully replaces the Collections table
    /// with its contents. Safe to call repeatedly (idempotent full-replace, not an upsert).
    /// </summary>
    public async Task<CollectionCatalogSyncResult> SyncFromBlobAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var result = new CollectionCatalogSyncResult { sourceBlobPath = opts.BlobPath };

        if (!opts.Enabled)
        {
            result.error = "Collection catalog sync is disabled (CollectionCatalog:Enabled=false).";
            return result;
        }

        List<CollectionEntry> entries;
        int skipped;
        try
        {
            await using var stream = await storage.OpenReadAsync(opts.BlobPath, ct);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            memory.Position = 0;

            using var workbook = new XLWorkbook(memory);
            var worksheet = string.IsNullOrEmpty(opts.SheetName)
                ? workbook.Worksheets.First()
                : workbook.Worksheet(opts.SheetName);

            (entries, skipped) = ParseWorksheet(worksheet);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync collections catalog from blob '{BlobPath}'.", opts.BlobPath);
            result.error = ex.Message;
            return result;
        }

        // Verify any Comments-column blob paths against the relevant storage container(s),
        // based on each row's Type (Audio → sound container, Text/Video → main container,
        // Audio & Text → both). Failures here must not abort the catalog sync.
        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Comments)))
        {
            entry.StoragePathVerified = await TryVerifyStoragePathAsync(entry.Comments!, entry.Type, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Collections.ExecuteDeleteAsync(ct);
        await db.Collections.AddRangeAsync(entries, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        result.success = true;
        result.rowsSynced = entries.Count;
        result.rowsSkipped = skipped;
        result.syncedAtUtc = DateTime.UtcNow;

        logger.LogInformation(
            "Collections catalog synced from '{BlobPath}': {Synced} rows synced, {Skipped} rows skipped.",
            opts.BlobPath, result.rowsSynced, result.rowsSkipped);

        return result;
    }

    private static (List<CollectionEntry> entries, int skipped) ParseWorksheet(IXLWorksheet worksheet)
    {
        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException($"Worksheet '{worksheet.Name}' has no header row.");

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (header.Length > 0)
                headerMap[header] = cell.Address.ColumnNumber;
        }

        var syncedAt = DateTime.UtcNow;
        var entries = new List<CollectionEntry>();
        var skipped = 0;

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var name = GetString(row, headerMap, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                skipped++;
                continue;
            }

            entries.Add(new CollectionEntry
            {
                Name          = name,
                Provider      = GetString(row, headerMap, "Provider"),
                Language      = GetString(row, headerMap, "Language"),
                Type          = GetString(row, headerMap, "Type"),
                Format        = GetString(row, headerMap, "Format"),
                Hours         = GetDouble(row, headerMap, "Hours"),
                Items         = GetInt(row, headerMap, "Items"),
                Description   = GetString(row, headerMap, "Description"),
                StartYear     = GetInt(row, headerMap, "Start Year"),
                EndYear       = GetInt(row, headerMap, "End Year"),
                Themes        = GetString(row, headerMap, "Themes"),
                Collected     = GetString(row, headerMap, "Collected"),
                Processing    = GetString(row, headerMap, "Processing"),
                Availability  = GetString(row, headerMap, "Availability"),
                Comments      = GetString(row, headerMap, "Comments"),
                TaggedForText = GetString(row, headerMap, "Tagged for Text"),
                SyncedAtUtc   = syncedAt,
            });
        }

        return (entries, skipped);
    }

    /// <summary>
    /// Checks whether the given Comments-column value exists as a blob prefix in the
    /// container(s) implied by the collection's Type. Returns false (rather than throwing)
    /// when the check fails, since this is a best-effort verification, not a sync blocker.
    /// </summary>
    private async Task<bool> TryVerifyStoragePathAsync(string rawPath, string? type, CancellationToken ct)
    {
        var prefix = rawPath.Trim().Trim('/');
        if (prefix.Length == 0) return false;

        var (checkMain, checkSound) = ResolveStorageTargets(type);

        try
        {
            if (checkMain && (await mainStorage.ListFilesAsync(prefix, ct)).Count > 0)
                return true;
            if (checkSound && (await soundStorage.ListFilesAsync(prefix, ct)).Count > 0)
                return true;
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not verify collection storage path '{Path}' (type: {Type}).", rawPath, type);
            return false;
        }
    }

    /// <summary>Maps a collection's Type to the storage container(s) its Comments path should
    /// be checked against: Audio → sound container; Text/Video (or unrecognized) → main
    /// container; anything mentioning both Audio and Text → both containers.</summary>
    private static (bool checkMain, bool checkSound) ResolveStorageTargets(string? type)
    {
        var t = (type ?? "").ToLowerInvariant();
        var hasAudio = t.Contains("audio");
        var hasText = t.Contains("text");

        if (hasAudio && hasText) return (true, true);
        if (hasAudio) return (false, true);
        return (true, false); // Text, Video, or unrecognized types default to the main container
    }

    // Header lookups tolerate trailing whitespace in workbook headers (e.g. "End Year ").
    private static string? GetString(IXLRow row, Dictionary<string, int> headerMap, string header)
    {
        var col = headerMap.Keys.FirstOrDefault(h => h.TrimEnd().Equals(header, StringComparison.OrdinalIgnoreCase));
        if (col is null) return null;
        var value = row.Cell(headerMap[col]).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static double? GetDouble(IXLRow row, Dictionary<string, int> headerMap, string header)
    {
        var raw = GetString(row, headerMap, header);
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static int? GetInt(IXLRow row, Dictionary<string, int> headerMap, string header)
    {
        var raw = GetString(row, headerMap, header);
        if (raw is null) return null;
        if (int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) return value;
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (int)d : null;
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public async Task<CatalogPagedResult<CollectionCatalogListItem>> ListAsync(
        CollectionCatalogQueryParams p, CancellationToken ct = default)
    {
        var query = db.Collections.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Q))
        {
            var q = p.Q.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(q) ||
                (c.Provider != null && c.Provider.ToLower().Contains(q)) ||
                (c.Description != null && c.Description.ToLower().Contains(q)));
        }

        if (!string.IsNullOrWhiteSpace(p.Type))
        {
            var t = p.Type.ToLower();
            query = query.Where(c => c.Type != null && c.Type.ToLower().Contains(t));
        }

        if (!string.IsNullOrWhiteSpace(p.Language))
        {
            var lang = p.Language.ToLower();
            query = query.Where(c => c.Language != null && c.Language.ToLower().Contains(lang));
        }

        if (!string.IsNullOrWhiteSpace(p.Provider))
        {
            var prov = p.Provider.ToLower();
            query = query.Where(c => c.Provider != null && c.Provider.ToLower().Contains(prov));
        }

        if (!string.IsNullOrWhiteSpace(p.Theme))
        {
            var theme = p.Theme.ToLower();
            query = query.Where(c => c.Themes != null && c.Themes.ToLower().Contains(theme));
        }

        if (!string.IsNullOrWhiteSpace(p.Collected))
            query = query.Where(c => c.Collected == p.Collected);

        if (!string.IsNullOrWhiteSpace(p.Processing))
            query = query.Where(c => c.Processing == p.Processing);

        if (!string.IsNullOrWhiteSpace(p.Availability))
            query = query.Where(c => c.Availability == p.Availability);

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

        var entries = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new CatalogPagedResult<CollectionCatalogListItem>
        {
            page = page,
            pageSize = pageSize,
            totalCount = totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            results = [.. entries.Select(ToListItem)],
        };
    }

    public async Task<CollectionCatalogDetail?> GetAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entry is null) return null;

        return new CollectionCatalogDetail
        {
            id = entry.Id,
            name = entry.Name,
            provider = entry.Provider,
            language = entry.Language,
            type = entry.Type,
            format = entry.Format,
            hours = entry.Hours,
            items = entry.Items,
            description = entry.Description,
            startYear = entry.StartYear,
            endYear = entry.EndYear,
            themes = SplitThemes(entry.Themes),
            collected = entry.Collected,
            processing = entry.Processing,
            availability = entry.Availability,
            taggedForText = entry.TaggedForText,
            storagePath = entry.Comments,
            storagePathVerified = entry.StoragePathVerified,
            syncedAtUtc = entry.SyncedAtUtc,
        };
    }

    public async Task<CollectionCatalogFacetsResponse> GetFacetsAsync(CancellationToken ct = default)
    {
        var totalCollections = await db.Collections.CountAsync(ct);

        var types = await db.Collections
            .Where(c => c.Type != null)
            .GroupBy(c => c.Type)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .ToListAsync(ct);

        var languages = await db.Collections
            .Where(c => c.Language != null)
            .GroupBy(c => c.Language)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .ToListAsync(ct);

        var providers = await db.Collections
            .Where(c => c.Provider != null)
            .GroupBy(c => c.Provider)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .ToListAsync(ct);

        var collected = await db.Collections
            .Where(c => c.Collected != null)
            .GroupBy(c => c.Collected)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .ToListAsync(ct);

        var processing = await db.Collections
            .Where(c => c.Processing != null)
            .GroupBy(c => c.Processing)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .ToListAsync(ct);

        var availability = await db.Collections
            .Where(c => c.Availability != null)
            .GroupBy(c => c.Availability)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .ToListAsync(ct);

        // Themes are stored as a comma-joined string per row, so counting distinct theme
        // values requires splitting them in memory rather than a SQL GROUP BY.
        var allThemes = await db.Collections
            .Where(c => c.Themes != null)
            .Select(c => c.Themes!)
            .ToListAsync(ct);

        var themes = allThemes
            .SelectMany(t => t.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FacetItem { value = g.Key, count = g.Count() })
            .OrderByDescending(f => f.count)
            .Take(30)
            .ToList();

        return new CollectionCatalogFacetsResponse
        {
            totalCollections = totalCollections,
            types = types,
            languages = languages,
            providers = providers,
            themes = themes,
            collected = collected,
            processing = processing,
            availability = availability,
        };
    }

    private static List<string> SplitThemes(string? themes) =>
        themes is null
            ? []
            : [.. themes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    private static CollectionCatalogListItem ToListItem(CollectionEntry c) => new()
    {
        id = c.Id,
        name = c.Name,
        provider = c.Provider,
        language = c.Language,
        type = c.Type,
        format = c.Format,
        hours = c.Hours,
        items = c.Items,
        startYear = c.StartYear,
        endYear = c.EndYear,
        themes = SplitThemes(c.Themes),
        collected = c.Collected,
        processing = c.Processing,
        availability = c.Availability,
        taggedForText = c.TaggedForText,
        hasStoragePath = !string.IsNullOrWhiteSpace(c.Comments),
        storagePathVerified = c.StoragePathVerified,
    };
}
