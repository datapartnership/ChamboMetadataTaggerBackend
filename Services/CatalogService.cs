using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

/// <summary>
/// Provides publicly-accessible catalog queries used by the CKAN-style catalog UI.
/// All methods return plain DTO objects; no authentication is required by callers.
/// </summary>
public class CatalogService(ApplicationDbContext db)
{
    // ── Text datasets ─────────────────────────────────────────────────────────

    public async Task<CatalogPagedResult<CatalogDatasetListItem>> ListDatasetsAsync(
        CatalogDatasetQueryParams p, CancellationToken ct = default)
    {
        p.Page     = Math.Max(1, p.Page);
        p.PageSize = Math.Clamp(p.PageSize, 1, 50);

        var query = db.Datasets.Include(d => d.Files)
                               .ThenInclude(f => f.Keywords)
                               .Include(d => d.Files)
                               .ThenInclude(f => f.Themes)
                               .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Q))
        {
            var q = p.Q.ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(q) ||
                (d.Publisher != null && d.Publisher.ToLower().Contains(q)) ||
                (d.Creator   != null && d.Creator.ToLower().Contains(q)));
        }

        if (!string.IsNullOrWhiteSpace(p.Keyword))
        {
            var kw = p.Keyword.ToLower();
            query = query.Where(d => d.Files.Any(f =>
                f.Keywords.Any(k => k.Value.ToLower().Contains(kw))));
        }

        if (!string.IsNullOrWhiteSpace(p.Theme))
        {
            var th = p.Theme.ToLower();
            query = query.Where(d => d.Files.Any(f =>
                f.Themes.Any(t => t.Value.ToLower().Contains(th))));
        }

        if (!string.IsNullOrWhiteSpace(p.Year))
        {
            query = query.Where(d => d.Id.EndsWith(p.Year) ||
                (d.DatePublished != null && d.DatePublished.StartsWith(p.Year)) ||
                (d.TemporalCoverage != null && d.TemporalCoverage.Contains(p.Year)));
        }

        var total = await query.CountAsync(ct);

        var datasets = await query
            .OrderByDescending(d => d.DatePublished ?? d.Id)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(ct);

        var items = datasets.Select(d => new CatalogDatasetListItem
        {
            id               = d.Id,
            name             = d.Name,
            publisher        = d.Publisher,
            creator          = d.Creator,
            datePublished    = d.DatePublished,
            temporalCoverage = d.TemporalCoverage,
            spatialCoverage  = d.SpatialCoverage,
            languageCode     = d.LanguageCode,
            license          = d.License,
            fileCount        = d.Files.Count,
            topKeywords      = d.Files.SelectMany(f => f.Keywords.Select(k => k.Value))
                                      .GroupBy(v => v).OrderByDescending(g => g.Count())
                                      .Take(5).Select(g => g.Key).ToList(),
            topThemes        = d.Files.SelectMany(f => f.Themes.Select(t => t.Value))
                                      .GroupBy(v => v).OrderByDescending(g => g.Count())
                                      .Take(5).Select(g => g.Key).ToList()
        }).ToList();

        return new CatalogPagedResult<CatalogDatasetListItem>
        {
            page       = p.Page,
            pageSize   = p.PageSize,
            totalCount = total,
            totalPages = (int)Math.Ceiling(total / (double)p.PageSize),
            results    = items
        };
    }

    public async Task<CatalogDatasetDetail?> GetDatasetAsync(string id, CancellationToken ct = default)
    {
        var dataset = await db.Datasets
            .Include(d => d.Files)
            .ThenInclude(f => f.Keywords)
            .Include(d => d.Files)
            .ThenInclude(f => f.Themes)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (dataset is null) return null;

        return new CatalogDatasetDetail
        {
            id               = dataset.Id,
            name             = dataset.Name,
            publisher        = dataset.Publisher,
            creator          = dataset.Creator,
            datePublished    = dataset.DatePublished,
            dateModified     = dataset.DateModified,
            temporalCoverage = dataset.TemporalCoverage,
            spatialCoverage  = dataset.SpatialCoverage,
            languageCode     = dataset.LanguageCode,
            license          = dataset.License,
            version          = dataset.Version,
            totalFiles       = dataset.Files.Count,
            files            = [],
            allKeywords      = dataset.Files.SelectMany(f => f.Keywords.Select(k => k.Value)).Distinct().OrderBy(v => v).ToList(),
            allThemes        = dataset.Files.SelectMany(f => f.Themes.Select(t => t.Value)).Distinct().OrderBy(v => v).ToList()
        };
    }

    public async Task<CatalogPagedResult<CatalogFileListItem>> ListFilesAsync(
        CatalogFileQueryParams p, CancellationToken ct = default)
    {
        p.Page     = Math.Max(1, p.Page);
        p.PageSize = Math.Clamp(p.PageSize, 1, 50);

        var query = db.FileRecords
            .Include(f => f.Dataset)
            .Include(f => f.Keywords)
            .Include(f => f.Themes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Q))
        {
            var q = p.Q.Trim();
            var qLower = q.ToLower();
            query = query.Where(f =>
                f.Name.ToLower().Contains(qLower) ||
                (f.Author != null && f.Author.ToLower().Contains(qLower)));
        }

        if (!string.IsNullOrWhiteSpace(p.Keyword))
        {
            var kw = p.Keyword.Trim();
            var kwLower = kw.ToLower();
            query = query.Where(f => f.Keywords.Any(k => k.Value.ToLower() == kwLower));
        }

        if (!string.IsNullOrWhiteSpace(p.Theme))
        {
            var th = p.Theme.Trim();
            var thLower = th.ToLower();
            query = query.Where(f => f.Themes.Any(t => t.Value.ToLower() == thLower));
        }

        if (!string.IsNullOrWhiteSpace(p.Year))
        {
            var year = p.Year.Trim();
            query = query.Where(f => f.DatePublished != null && f.DatePublished.StartsWith(year));
        }

        if (!string.IsNullOrWhiteSpace(p.Author))
        {
            var author = p.Author.Trim();
            var authorLower = author.ToLower();
            query = query.Where(f => f.Author != null && f.Author.ToLower().Contains(authorLower));
        }

        if (!string.IsNullOrWhiteSpace(p.DatasetId))
        {
            query = query.Where(f => f.DatasetId == p.DatasetId);
        }

        var total = await query.CountAsync(ct);

        var files = await query
            .OrderByDescending(f => f.DatePublished ?? f.Id)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(ct);

        var items = files.Select(f => new CatalogFileListItem
        {
            id            = f.Id,
            datasetId     = f.DatasetId,
            datasetName   = f.Dataset?.Name ?? "",
            name          = f.Name,
            author        = f.Author,
            datePublished = f.DatePublished,
            encodingFormat = f.EncodingFormat,
            contentSize   = f.ContentSize,
            wordCount     = f.WordCount,
            charCount     = f.CharCount,
            tokenCount    = f.TokenCount,
            numberOfPages = f.NumberOfPages,
            subject       = f.Subject,
            discourseType = f.DiscourseType,
            keywords      = f.Keywords.Select(k => k.Value).OrderBy(v => v).ToList(),
            themes        = f.Themes.Select(t => t.Value).OrderBy(v => v).ToList()
        }).ToList();

        return new CatalogPagedResult<CatalogFileListItem>
        {
            page       = p.Page,
            pageSize   = p.PageSize,
            totalCount = total,
            totalPages = (int)Math.Ceiling(total / (double)p.PageSize),
            results    = items
        };
    }

    // ── Sound datasets ────────────────────────────────────────────────────────

    public async Task<CatalogPagedResult<CatalogSoundDatasetListItem>> ListSoundDatasetsAsync(
        CatalogSoundQueryParams p, CancellationToken ct = default)
    {
        p.Page     = Math.Max(1, p.Page);
        p.PageSize = Math.Clamp(p.PageSize, 1, 50);

        var query = db.SoundDatasets.Include(d => d.Files).AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Q))
        {
            var q = p.Q.ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(q) ||
                (d.CreatorName != null && d.CreatorName.ToLower().Contains(q)) ||
                (d.Description != null && d.Description.ToLower().Contains(q)));
        }

        if (!string.IsNullOrWhiteSpace(p.Language))
        {
            var lang = p.Language.ToLower();
            query = query.Where(d =>
                (d.SubjectLanguageCode != null && d.SubjectLanguageCode.ToLower().Contains(lang)) ||
                (d.SubjectLanguageName != null && d.SubjectLanguageName.ToLower().Contains(lang)) ||
                (d.InLanguageIdentifier != null && d.InLanguageIdentifier.ToLower().Contains(lang)) ||
                (d.InLanguageName != null && d.InLanguageName.ToLower().Contains(lang)));
        }

        if (!string.IsNullOrWhiteSpace(p.Year))
        {
            query = query.Where(d =>
                (d.DatePublished != null && d.DatePublished.StartsWith(p.Year)) ||
                (d.TemporalCoverage != null && d.TemporalCoverage.Contains(p.Year)));
        }

        var total = await query.CountAsync(ct);

        var datasets = await query
            .OrderByDescending(d => d.DatePublished ?? d.Id)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(ct);

        var items = datasets.Select(d => new CatalogSoundDatasetListItem
        {
            id                   = d.Id,
            name                 = d.Name,
            creator              = d.CreatorName,
            provider             = d.ProviderName,
            datePublished        = d.DatePublished,
            temporalCoverage     = d.TemporalCoverage,
            spatialCoverage      = d.SpatialCoverage,
            subjectLanguageCode  = d.SubjectLanguageCode,
            subjectLanguageName  = d.SubjectLanguageName,
            inLanguageName       = d.InLanguageName,
            license              = d.License,
            description          = d.Description,
            fileCount            = d.Files.Count,
            keywords             = string.IsNullOrWhiteSpace(d.Keywords)
                                    ? []
                                    : d.Keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
        }).ToList();

        return new CatalogPagedResult<CatalogSoundDatasetListItem>
        {
            page       = p.Page,
            pageSize   = p.PageSize,
            totalCount = total,
            totalPages = (int)Math.Ceiling(total / (double)p.PageSize),
            results    = items
        };
    }

    public async Task<CatalogSoundDatasetDetail?> GetSoundDatasetAsync(string id, CancellationToken ct = default)
    {
        var dataset = await db.SoundDatasets
            .Include(d => d.Files)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (dataset is null) return null;

        return new CatalogSoundDatasetDetail
        {
            id                    = dataset.Id,
            name                  = dataset.Name,
            description           = dataset.Description,
            creator               = dataset.CreatorName,
            provider              = dataset.ProviderName,
            sourceOrganization    = dataset.SourceOrganizationName,
            datePublished         = dataset.DatePublished,
            dateCreated           = dataset.DateCreated,
            dateModified          = dataset.DateModified,
            temporalCoverage      = dataset.TemporalCoverage,
            spatialCoverage       = dataset.SpatialCoverage,
            subjectLanguageCode   = dataset.SubjectLanguageCode,
            subjectLanguageName   = dataset.SubjectLanguageName,
            inLanguageName        = dataset.InLanguageName,
            inLanguageIdentifier  = dataset.InLanguageIdentifier,
            license               = dataset.License,
            version               = dataset.Version,
            piiScreening          = dataset.PiiScreening,
            sensitiveContent      = dataset.SensitiveContent,
            keywords              = string.IsNullOrWhiteSpace(dataset.Keywords)
                                     ? []
                                     : dataset.Keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
            totalFiles            = dataset.Files.Count,
            files = dataset.Files.Select(f => new CatalogSoundFileItem
            {
                id                   = f.Id,
                name                 = f.Name,
                encodingFormat       = f.EncodingFormat,
                contentSize          = f.ContentSize,
                duration             = f.Duration,
                sampleRate           = f.SampleRate,
                channels             = f.Channels,
                bitrate              = f.Bitrate,
                integratedLoudness   = f.IntegratedLoudness,
                signalToNoiseRatio   = f.SignalToNoiseRatio,
                dateModified         = f.DateModified
            }).OrderBy(f => f.name).ToList()
        };
    }

    // ── Facets ────────────────────────────────────────────────────────────────

    public async Task<CatalogFacetsResponse> GetFacetsAsync(CancellationToken ct = default)
    {
        var totalFiles = await db.FileRecords.CountAsync(ct);
        var taggedFiles = await db.FileRecords
            .Where(f => f.Keywords.Any() || f.Themes.Any())
            .CountAsync(ct);

        var topKeywords = await db.Keywords
            .GroupBy(k => k.Value)
            .Select(g => new FacetItem { value = g.Key, count = g.Count() })
            .OrderByDescending(f => f.count)
            .Take(30)
            .ToListAsync(ct);

        var topThemes = await db.Themes
            .GroupBy(t => t.Value)
            .Select(g => new FacetItem { value = g.Key, count = g.Count() })
            .OrderByDescending(f => f.count)
            .Take(20)
            .ToListAsync(ct);

        var authorFacets = await db.FileRecords
            .Where(f => !string.IsNullOrWhiteSpace(f.Author))
            .GroupBy(f => f.Author)
            .Select(g => new FacetItem { value = g.Key ?? "", count = g.Count() })
            .OrderByDescending(f => f.count)
            .Take(20)
            .ToListAsync(ct);

        var fileYears = await db.FileRecords
            .Where(f => f.DatePublished != null)
            .Select(f => f.DatePublished!.Substring(0, 4))
            .Distinct()
            .ToListAsync(ct);

        var soundYears = await db.SoundDatasets
            .Where(d => d.DatePublished != null)
            .Select(d => d.DatePublished!.Substring(0, 4))
            .Distinct()
            .ToListAsync(ct);

        var allYears = fileYears.Concat(soundYears).Distinct().OrderByDescending(y => y).ToList();

        var soundLanguages = await db.SoundDatasets
            .Where(d => d.SubjectLanguageCode != null)
            .GroupBy(d => new { d.SubjectLanguageCode, d.SubjectLanguageName })
            .Select(g => new FacetItem
            {
                value = g.Key.SubjectLanguageName ?? g.Key.SubjectLanguageCode ?? "",
                count = g.Count()
            })
            .OrderByDescending(f => f.count)
            .Take(20)
            .ToListAsync(ct);

        return new CatalogFacetsResponse
        {
            totalFiles = totalFiles,
            taggedFiles = taggedFiles,
            keywords = topKeywords,
            themes = topThemes,
            authors = authorFacets,
            years = allYears,
            languages = soundLanguages
        };
    }
}
