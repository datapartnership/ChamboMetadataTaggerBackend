using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

/// <summary>
/// Queries <see cref="SoundFileRecord"/> rows with optional filters and applies
/// the same random-sampling strategy as <see cref="FileQueryService"/>.
/// </summary>
public class SoundQueryService(ApplicationDbContext db)
{
    /// <summary>
    /// Returns matching sound files grouped by dataset. Filtered queries are
    /// randomly sampled to their limit; unfiltered queries are randomly sampled to 80%.
    /// </summary>
    public async Task<(List<(SoundDataset Dataset, SoundFileRecord[] Files)> Groups, int TotalMatched, int AppliedLimit)>
        QueryAsync(SoundDatasetQueryParams p)
    {
        var hasFilters =
            !string.IsNullOrWhiteSpace(p.Language) ||
            !string.IsNullOrWhiteSpace(p.Programme) ||
            !string.IsNullOrWhiteSpace(p.Keyword) ||
            p.Year.HasValue;

        var query = db.SoundFileRecords
            .Include(f => f.Dataset)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Language))
        {
            var lang = p.Language.ToLower();
            query = query.Where(f =>
                f.Dataset != null && (
                    (f.Dataset.SubjectLanguageCode != null && f.Dataset.SubjectLanguageCode.ToLower().Contains(lang)) ||
                    (f.Dataset.InLanguageIdentifier != null && f.Dataset.InLanguageIdentifier.ToLower().Contains(lang))
                ));
        }

        if (!string.IsNullOrWhiteSpace(p.Programme))
        {
            var prog = p.Programme.ToLower();
            query = query.Where(f =>
                f.Dataset != null && f.Dataset.Name.ToLower().Contains(prog));
        }

        if (!string.IsNullOrWhiteSpace(p.Keyword))
        {
            var kw = p.Keyword.ToLower();
            query = query.Where(f =>
                f.Dataset != null &&
                f.Dataset.Keywords != null &&
                f.Dataset.Keywords.ToLower().Contains(kw));
        }

        if (p.Year.HasValue)
        {
            var yr = p.Year.Value.ToString();
            query = query.Where(f =>
                f.Dataset != null && (
                    (f.Dataset.DatePublished != null && f.Dataset.DatePublished.StartsWith(yr)) ||
                    (f.Dataset.TemporalCoverage != null && f.Dataset.TemporalCoverage.Contains(yr))
                ));
        }

        var all = await query.ToListAsync();
        var total = all.Count;
        var limit = p.Limit ?? (hasFilters ? 30 : (int)Math.Ceiling(total * 0.8));

        var sampled = total <= limit
            ? all
            : all.OrderBy(_ => Guid.NewGuid()).Take(limit).ToList();

        var grouped = sampled
            .GroupBy(f => f.Dataset!)
            .Select(g => (Dataset: g.Key, Files: g.ToArray()))
            .ToList();

        return (grouped, total, limit);
    }
}
