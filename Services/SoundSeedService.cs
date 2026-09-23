using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetadataTagging.Data;
using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

/// <summary>
/// Scans the sound storage bucket for <c>*_metadata.jsonld</c> files,
/// parses them, and populates <see cref="Data.Models.SoundDataset"/> /
/// <see cref="Data.Models.SoundFileRecord"/> rows in the database.
/// The operation is idempotent — existing records (matched by ContentUrl) are skipped.
/// </summary>
public class SoundSeedService(
    ApplicationDbContext db,
    [FromKeyedServices("sound")] IFileStorageService soundStorage,
    SoundMetadataParser parser)
{
    public async Task<SoundSeedResult> SeedAsync(CancellationToken ct = default)
    {
        var allEntries = await soundStorage.ListFilesAsync("", ct);

        var metadataKeys = allEntries
            .Where(e => e.Key.EndsWith("_metadata.jsonld", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int datasetsAdded = 0, filesAdded = 0, filesSkipped = 0;

        foreach (var entry in metadataKeys)
        {
            // Derive publisher folder prefix from metadata file path
            // (e.g. "Trans World Radio/kbj_dataset_metadata.jsonld" → "Trans World Radio").
            // Used only as a fallback when a distribution entry lacks sc:contentUrl.
            var folderPrefix = Path.GetDirectoryName(entry.Key)?.Replace('\\', '/') ?? "";

            string json;
            await using (var stream = await soundStorage.OpenReadAsync(entry.Key, ct))
            using (var reader = new StreamReader(stream))
                json = await reader.ReadToEndAsync(ct);

            var (dataset, files) = parser.Parse(json, folderPrefix);

            if (!await db.SoundDatasets.AnyAsync(d => d.Id == dataset.Id, ct))
            {
                db.SoundDatasets.Add(dataset);
                datasetsAdded++;
            }

            foreach (var file in files)
            {
                if (await db.SoundFileRecords.AnyAsync(f => f.ContentUrl == file.ContentUrl, ct))
                {
                    filesSkipped++;
                }
                else
                {
                    db.SoundFileRecords.Add(file);
                    filesAdded++;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return new SoundSeedResult
        {
            DatasetsAdded = datasetsAdded,
            FilesAdded    = filesAdded,
            FilesSkipped  = filesSkipped,
            Message       = $"Sound seed complete: {datasetsAdded} dataset(s) added, " +
                            $"{filesAdded} file(s) added, {filesSkipped} file(s) skipped."
        };
    }
}
