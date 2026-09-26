using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Provides aggregated summaries of collections, files, keywords, and themes
/// across all media types.
/// </summary>
public class SummaryService(ApplicationDbContext db)
{
    public async Task<CollectionsSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var collections = await db.Collections
            .GroupBy(c => c.MediaType)
            .Select(g => new
            {
                MediaType = g.Key,
                Count     = g.Count(),
                Files     = g.Sum(c => c.FileCount),
                Duration  = g.Sum(c => c.TotalDurationSeconds ?? 0),
            })
            .ToListAsync(cancellationToken);

        var files = await db.CollectionFiles
            .GroupBy(f => new { f.Collection!.MediaType, f.Kind })
            .Select(g => new
            {
                g.Key.MediaType,
                g.Key.Kind,
                Tagged = g.Count(f => f.Keywords.Any() || f.Themes.Any()),
                Count  = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var byMediaType = Enum.GetValues<MediaType>().Select(mediaType =>
        {
            var c = collections.FirstOrDefault(x => x.MediaType == mediaType);
            var f = files.Where(x => x.MediaType == mediaType).ToList();
            return new MediaTypeSummary
            {
                mediaType            = mediaType.ToString(),
                collectionCount      = c?.Count ?? 0,
                fileCount            = c?.Files ?? 0,
                transcriptionCount   = f.Where(x => x.Kind == FileKind.Transcription).Sum(x => x.Count),
                taggedFileCount      = f.Sum(x => x.Tagged),
                totalDurationSeconds = c?.Duration ?? 0,
            };
        }).ToList();

        var topKeywords = await db.Keywords
            .GroupBy(k => k.Value)
            .Select(g => new KeywordThemeSummary
            {
                value = g.Key,
                count = g.Count(),
                fileCount = g.Select(k => k.FileId).Distinct().Count()
            })
            .OrderByDescending(k => k.count)
            .Take(20)
            .ToListAsync(cancellationToken);

        var topThemes = await db.Themes
            .GroupBy(t => t.Value)
            .Select(g => new KeywordThemeSummary
            {
                value = g.Key,
                count = g.Count(),
                fileCount = g.Select(t => t.FileId).Distinct().Count()
            })
            .OrderByDescending(t => t.count)
            .Take(20)
            .ToListAsync(cancellationToken);

        var topProviders = await db.Collections
            .GroupBy(c => c.Provider)
            .Select(g => new PublisherSummary { name = g.Key, fileCount = g.Sum(c => c.FileCount) })
            .OrderByDescending(p => p.fileCount)
            .Take(10)
            .ToListAsync(cancellationToken);

        return new CollectionsSummaryResponse
        {
            generatedAt      = DateTime.UtcNow,
            totalCollections = byMediaType.Sum(m => m.collectionCount),
            totalFiles       = byMediaType.Sum(m => m.fileCount),
            totalTaggedFiles = byMediaType.Sum(m => m.taggedFileCount),
            topProviders     = topProviders,
            topKeywords      = topKeywords,
            topThemes        = topThemes,
            byMediaType      = byMediaType,
        };
    }
}
