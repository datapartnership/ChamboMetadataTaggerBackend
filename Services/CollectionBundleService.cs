using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Builds ZIP bundles that keep audio/video files together with their transcriptions:
/// a single media file + its transcription, or a whole audio/video collection.
/// Text files are never bundled; they are only served as shuffled paragraphs.
/// </summary>
public class CollectionBundleService(
    ApplicationDbContext db,
    IFileStorageService storage,
    CroissantBuilder builder,
    ILogger<CollectionBundleService> logger)
{
    /// <summary>
    /// Returns a bundle with the media file and its paired transcription. Passing a
    /// transcription Id bundles it with the media file it belongs to.
    /// Returns <c>null</c> when no audio, video, or transcription record exists for <paramref name="id"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// The media file or its linked transcription is missing from storage. Both are opened
    /// before streaming starts so the caller can still answer 404.
    /// </exception>
    public async Task<ZipBundle?> GetFileBundleAsync(string id, CancellationToken ct = default)
    {
        var record = await db.CollectionFiles.FirstOrDefaultAsync(f => f.Id == id && f.Kind != FileKind.Text, ct);
        if (record is null)
            return null;

        var isTranscription = record.Kind == FileKind.Transcription;
        var media = isTranscription
            ? await db.CollectionFiles.FirstOrDefaultAsync(f => f.TranscriptionFileId == record.Id, ct)
            : record;

        var transcription = isTranscription
            ? record
            : record.TranscriptionFileId is { } transcriptionId
                ? await db.CollectionFiles.FirstOrDefaultAsync(f => f.Id == transcriptionId, ct)
                : null;

        var members = new[] { media, transcription }.OfType<CollectionFile>().ToList();
        var opened  = new List<(CollectionFile File, Stream Stream)>();
        try
        {
            foreach (var file in members)
                opened.Add((file, await storage.OpenReadAsync(file.ContentUrl, ct)));
        }
        catch
        {
            foreach (var (_, stream) in opened)
                await stream.DisposeAsync();
            throw;
        }

        var zipName = $"{Path.GetFileNameWithoutExtension(SafeEntryName(media ?? record))}.zip";

        return new ZipBundle(zipName, async (zip, token) =>
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, stream) in opened)
            {
                await using (stream)
                    await AddEntryAsync(zip, UniqueEntryName(file, names), stream, file, token);
            }
        });
    }

    /// <summary>
    /// Returns a bundle with every media file and transcription in an audio/video collection,
    /// plus a <c>metadata.jsonld</c> Croissant document describing the pairs.
    /// Files missing from storage are skipped and listed in <c>MISSING_FILES.txt</c>.
    /// Returns <c>null</c> when no audio/video collection with that Id exists.
    /// </summary>
    public async Task<ZipBundle?> GetCollectionBundleAsync(string collectionId, string? baseUrl, CancellationToken ct = default)
    {
        var collection = await db.Collections
            .Include(c => c.Files).ThenInclude(f => f.Keywords)
            .Include(c => c.Files).ThenInclude(f => f.Themes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == collectionId && c.MediaType != MediaType.Text, ct);
        if (collection is null)
            return null;

        var media = collection.Files.Where(f => f.Kind != FileKind.Transcription).OrderBy(f => f.Name).ToArray();
        var transcriptions = collection.Files.Where(f => f.Kind == FileKind.Transcription).ToDictionary(f => f.Id);

        var metadata = builder.Build([(collection, media)], media.Length, media.Length, baseUrl, transcriptions);

        // Each transcription goes right after its media file; unpaired transcriptions go at the end.
        var ordered = new List<CollectionFile>();
        foreach (var m in media)
        {
            ordered.Add(m);
            if (m.TranscriptionFileId != null && transcriptions.Remove(m.TranscriptionFileId, out var t))
                ordered.Add(t);
        }
        ordered.AddRange(transcriptions.Values.OrderBy(t => t.Name));

        var zipName = $"{SanitizeFileName(collection.Name, collection.Id)}.zip";

        return new ZipBundle(zipName, async (zip, token) =>
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "metadata.jsonld", "MISSING_FILES.txt" };
            await WriteTextEntryAsync(zip, "metadata.jsonld", metadata, token);

            var missing = new List<string>();
            foreach (var file in ordered)
            {
                Stream stream;
                try
                {
                    stream = await storage.OpenReadAsync(file.ContentUrl, token);
                }
                catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Bundle for collection {CollectionId}: '{ContentUrl}' is missing from storage",
                        collection.Id, file.ContentUrl);
                    missing.Add(file.ContentUrl);
                    continue;
                }

                await using (stream)
                    await AddEntryAsync(zip, UniqueEntryName(file, names), stream, file, token);
            }

            if (missing.Count > 0)
                await WriteTextEntryAsync(zip, "MISSING_FILES.txt", string.Join('\n', missing) + "\n", token);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task AddEntryAsync(
        ZipArchive zip, string entryName, Stream source, CollectionFile file, CancellationToken ct)
    {
        // Audio/video is already compressed; deflating it again only costs CPU.
        var level = file.Kind == FileKind.Transcription ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
        var entry = zip.CreateEntry(entryName, level);
        await using var target = entry.Open();
        await source.CopyToAsync(target, ct);
    }

    private static async Task WriteTextEntryAsync(ZipArchive zip, string entryName, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await target.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
    }

    private static string SafeEntryName(CollectionFile file) =>
        SanitizeFileName(Path.GetFileName(file.Name.Replace('\\', '/')), file.Id);

    private static string UniqueEntryName(CollectionFile file, HashSet<string> used)
    {
        var name = SafeEntryName(file);
        return used.Add(name) ? name : $"{file.Id}_{name}";
    }

    private static string SanitizeFileName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 || cleaned is "." or ".." ? fallback : cleaned;
    }
}
