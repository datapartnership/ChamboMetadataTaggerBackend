using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

/// <summary>
/// Provides aggregated summaries of datasets, files, keywords, and themes
/// across both text and sound pipelines.
/// </summary>
public class SummaryService(ApplicationDbContext db)
{
    /// <summary>
    /// Gets a summary of all datasets, including file counts, tagged file counts,
    /// top publishers, keywords, and themes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated datasets summary</returns>
    public async Task<DatasetsSummaryResponse> GetDatasetsSummaryAsync(CancellationToken cancellationToken = default)
    {
        // Text pipeline aggregations
        var textDatasetCount = await db.Datasets.CountAsync(cancellationToken);
        var textFileCount = await db.FileRecords.CountAsync(cancellationToken);
        var textTaggedFileCount = await db.FileRecords
            .Where(f => f.Keywords.Any() || f.Themes.Any())
            .CountAsync(cancellationToken);

        // Sound pipeline aggregations
        var soundDatasetCount = await db.SoundDatasets.CountAsync(cancellationToken);
        var soundFileCount = await db.SoundFileRecords.CountAsync(cancellationToken);
        // Sound files are not tagged, so taggedFileCount is 0
        var soundTaggedFileCount = 0;

        // Top keywords (top 20, sorted by count descending)
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

        // Top themes (top 20, sorted by count descending)
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

        // Top publishers/creators from text datasets (top 10 by file count)
        var textPublishers = await db.Datasets
            .GroupBy(d => d.Publisher ?? d.Creator ?? "Unknown")
            .Select(g => new PublisherSummary
            {
                name = g.Key,
                fileCount = g.Sum(d => d.Files.Count)
            })
            .OrderByDescending(p => p.fileCount)
            .Take(10)
            .ToListAsync(cancellationToken);

        // Top creators from sound datasets (top 10 by file count)
        var soundCreators = await db.SoundDatasets
            .Where(sd => sd.CreatorName != null)
            .GroupBy(sd => sd.CreatorName ?? "Unknown")
            .Select(g => new PublisherSummary
            {
                name = g.Key,
                fileCount = g.Sum(d => d.Files.Count)
            })
            .OrderByDescending(p => p.fileCount)
            .Take(10)
            .ToListAsync(cancellationToken);

        // Combine and merge publishers from both pipelines, deduplicate by name, and take top 10
        var allPublishers = textPublishers.Concat(soundCreators)
            .GroupBy(p => p.name)
            .Select(g => new PublisherSummary
            {
                name = g.Key,
                fileCount = g.Sum(p => p.fileCount)
            })
            .OrderByDescending(p => p.fileCount)
            .Take(10)
            .ToList();

        // Build response
        var response = new DatasetsSummaryResponse
        {
            generatedAt = DateTime.UtcNow,
            totalDatasets = textDatasetCount + soundDatasetCount,
            totalFiles = textFileCount + soundFileCount,
            totalTaggedFiles = textTaggedFileCount + soundTaggedFileCount,
            topPublishers = allPublishers,
            topKeywords = topKeywords,
            topThemes = topThemes,
            textSummary = new TextPipelineSummary
            {
                datasetCount = textDatasetCount,
                fileCount = textFileCount,
                taggedFileCount = textTaggedFileCount
            },
            soundSummary = new SoundPipelineSummary
            {
                datasetCount = soundDatasetCount,
                fileCount = soundFileCount,
                taggedFileCount = soundTaggedFileCount
            }
        };

        return response;
    }
}
