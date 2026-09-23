using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Parses a Croissant JSON-LD metadata file (produced alongside FLAC/audio files)
/// into <see cref="SoundDataset"/> and <see cref="SoundFileRecord"/> entities.
/// </summary>
public class SoundMetadataParser
{
    /// <summary>
    /// Parses the supplied JSON-LD text and returns a populated dataset with its files.
    /// </summary>
    /// <param name="json">Contents of a <c>*_metadata.jsonld</c> file.</param>
    /// <param name="folderPrefix">
    /// Publisher folder prefix within the sound bucket (e.g. <c>Trans World Radio</c>).
    /// Used as a fallback storage-key prefix when a distribution entry lacks
    /// <c>sc:contentUrl</c>. When <c>sc:contentUrl</c> is present it is used directly
    /// (leading <c>/</c> stripped) and <paramref name="folderPrefix"/> is ignored.
    /// </param>
    public (SoundDataset Dataset, List<SoundFileRecord> Files) Parse(string json, string folderPrefix)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dataset = new SoundDataset
        {
            Id                   = GetString(root, "sc:identifier") ?? GenerateDatasetId(folderPrefix),
            Name                 = GetString(root, "sc:name") ?? folderPrefix,
            Description          = GetString(root, "sc:description"),
            Version              = GetString(root, "sc:version"),
            DatePublished        = GetString(root, "sc:datePublished"),
            DateCreated          = GetString(root, "sc:dateCreated"),
            DateModified         = GetString(root, "sc:dateModified"),
            TemporalCoverage     = GetString(root, "sc:temporalCoverage"),
            SpatialCoverage      = GetString(root, "sc:spatialCoverage"),
            License              = GetString(root, "sc:license"),
            PiiScreening         = GetBool(root, "ddpv:piiScreening"),
            SensitiveContent     = GetBool(root, "ddpv:sensitiveContent"),
        };

        if (root.TryGetProperty("olac:subjectLanguage", out var olac))
        {
            dataset.SubjectLanguageName = GetString(olac, "name");
            dataset.SubjectLanguageCode = GetString(olac, "code");
        }

        if (root.TryGetProperty("sc:inLanguage", out var inLang))
        {
            dataset.InLanguageName       = GetString(inLang, "name");
            dataset.InLanguageIdentifier = GetString(inLang, "identifier");
        }

        if (root.TryGetProperty("sc:creator", out var creator))
            dataset.CreatorName = creator.ValueKind == JsonValueKind.String
                ? creator.GetString()
                : GetString(creator, "sc:name");

        if (root.TryGetProperty("sc:provider", out var provider))
            dataset.ProviderName = provider.ValueKind == JsonValueKind.String
                ? provider.GetString()
                : GetString(provider, "sc:name");

        if (root.TryGetProperty("sc:sourceOrganization", out var srcOrg))
            dataset.SourceOrganizationName = srcOrg.ValueKind == JsonValueKind.String
                ? srcOrg.GetString()
                : GetString(srcOrg, "sc:name");

        if (root.TryGetProperty("sc:keywords", out var kw))
        {
            dataset.Keywords = kw.ValueKind switch
            {
                JsonValueKind.Array  => string.Join(",", kw.EnumerateArray()
                                            .Select(e => e.GetString() ?? "")
                                            .Where(s => s.Length > 0)),
                JsonValueKind.String => kw.GetString(),
                _                    => null
            };
        }

        var files = new List<SoundFileRecord>();

        if (root.TryGetProperty("distribution", out var dist) && dist.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in dist.EnumerateArray())
            {
                var name       = GetString(entry, "sc:name") ?? "";
                var rawUrl     = GetString(entry, "sc:contentUrl");
                var contentKey = !string.IsNullOrEmpty(rawUrl)
                    ? rawUrl.TrimStart('/')
                    : string.IsNullOrEmpty(folderPrefix)
                        ? name
                        : $"{folderPrefix.TrimEnd('/')}/{name}";

                files.Add(new SoundFileRecord
                {
                    Id              = GenerateFileId(dataset.Id, name),
                    SoundDatasetId  = dataset.Id,
                    Name            = name,
                    ContentUrl      = contentKey,
                    EncodingFormat  = GetString(entry, "sc:encodingFormat") ?? "audio/flac",
                    ContentSize     = GetLong(entry, "sc:contentSize"),
                    DateModified    = GetString(entry, "sc:dateModified"),
                    Sha256          = GetString(entry, "cr:sha256"),
                    Duration        = GetDouble(entry, "ebucore:duration"),
                    SampleRate      = GetDouble(entry, "ebucore:sampleRate"),
                    Channels        = GetInt(entry, "ebucore:channels"),
                    Bitrate         = GetDouble(entry, "ebucore:bitrate"),
                    IntegratedLoudness  = GetDoubleOrNull(entry, "ebucore:integratedLoudness"),
                    SignalToNoiseRatio  = GetDoubleOrNull(entry, "ebucore:signalToNoiseRatio"),
                });
            }
        }

        return (dataset, files);
    }

    // ── ID generation ─────────────────────────────────────────────────────────

    private static string GenerateDatasetId(string folderPrefix)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(folderPrefix));
        return $"sound-ds-{Convert.ToHexString(bytes)[..8].ToLower()}";
    }

    private static string GenerateFileId(string datasetId, string fileName)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{datasetId}:{fileName}"));
        return $"sound-{Convert.ToHexString(bytes)[..8].ToLower()}";
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool GetBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static double GetDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetDouble(out var d) ? d : 0.0;

    private static double? GetDoubleOrNull(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetDouble(out var d) ? d : null;

    private static int GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static long? GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt64(out var l) ? l : null;
}
