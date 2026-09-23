using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

public class FileQueryService(ApplicationDbContext db)
{
    // Tagger statuses considered "ready": SubmittedToSupervisor and ApprovedBySupervisor.
    // FileMetadata/FileTags now live in the same ApplicationDbContext/database as the
    // catalog tables, so no separate (read-only) tagger database connection is needed.
    private static readonly FileTaggingStatus[] ReadyStatuses =
        [FileTaggingStatus.SubmittedToSupervisor, FileTaggingStatus.ApprovedBySupervisor];

    /// <summary>
    /// Returns all FileRecords matching the supplied filters. Filtered queries are
    /// randomly sampled to their limit; unfiltered queries are randomly sampled to 80%.
    /// Also returns tagger-app tags for the sampled files, keyed by ContentUrl.
    /// </summary>
    public async Task<(List<FileRecord> Sampled, int TotalMatched, int AppliedLimit, Dictionary<string, List<FileTag>> TaggerTags)> QueryAsync(DatasetQueryParams p)
    {
        var hasFilters =
            !string.IsNullOrWhiteSpace(p.Keyword) ||
            !string.IsNullOrWhiteSpace(p.Theme) ||
            !string.IsNullOrWhiteSpace(p.Author) ||
            !string.IsNullOrWhiteSpace(p.Year);

        var query = db.FileRecords
            .Include(f => f.Keywords)
            .Include(f => f.Themes)
            .Include(f => f.Dataset)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Keyword))
        {
            var kw = p.Keyword.ToLower();
            query = query.Where(f =>
                f.Keywords.Any(k => k.Value.ToLower().Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(p.Theme))
        {
            var th = p.Theme.ToLower();
            query = query.Where(f =>
                f.Themes.Any(t => t.Value.ToLower().Contains(th)));
        }

        if (!string.IsNullOrWhiteSpace(p.Author))
        {
            var auth = p.Author.ToLower();
            query = query.Where(f =>
                f.Author != null && f.Author.ToLower().Contains(auth));
        }

        if (!string.IsNullOrWhiteSpace(p.Year))
        {
            query = query.Where(f => f.DatasetId.EndsWith(p.Year));
        }

        // Fetch all matches (needed for random sampling)
        var all = await query.ToListAsync();
        var total = all.Count;
        var limit = p.Limit ?? (hasFilters ? 30 : (int)Math.Ceiling(total * 0.8));

        // Shuffle selected metadata before returning it; this does not affect file content.
        var sampled = total <= limit
            ? all
            : all.OrderBy(_ => Guid.NewGuid()).Take(limit).ToList();

        // Load tagger tags for the sampled files (matched by BlobName = ContentUrl)
        var taggerTags = await LoadTaggerTagsAsync(sampled);

        return (sampled, total, limit, taggerTags);
    }

    private async Task<Dictionary<string, List<FileTag>>> LoadTaggerTagsAsync(List<FileRecord> files)
    {
        var contentUrls = files
            .Select(f => f.ContentUrl)
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList();

        if (contentUrls.Count == 0)
            return [];

        var metadata = await db.FileMetadata
            .Include(m => m.Tags)
            .Where(m => m.BlobName != null
                        && contentUrls.Contains(m.BlobName)
                        && ReadyStatuses.Contains(m.Status))
            .ToListAsync();

        return metadata
            .Where(m => m.BlobName != null)
            .ToDictionary(m => m.BlobName!, m => m.Tags.ToList());
    }
}
