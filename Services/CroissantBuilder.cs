using System.Text.Json;
using System.Text.Json.Serialization;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Builds a Croissant 1.0 JSON-LD document from collection files grouped by collection:
/// one <c>sc:Dataset</c> node per collection inside <c>@graph</c>. Text files carry text
/// statistics and tagger tags; audio/video files carry EBU Core technical metadata, their
/// paired transcription and any tagger keywords/themes, and the collection carries DQV
/// quality measurements.
/// </summary>
public class CroissantBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Build(
        List<(Collection Collection, CollectionFile[] Files)> groups,
        int totalMatched,
        int limit,
        string? baseUrl = null,
        IReadOnlyDictionary<string, CollectionFile>? transcriptions = null,
        Dictionary<string, List<FileTag>>? taggerTags = null)
    {
        transcriptions ??= new Dictionary<string, CollectionFile>();
        var returned = groups.Sum(g => g.Files.Length);

        var context = new object[]
        {
            "https://mlcommons.org/working-groups/data/croissant/",
            new Dictionary<string, string>
            {
                ["sc"]      = "https://schema.org/",
                ["dct"]     = "http://purl.org/dc/terms/",
                ["dcat"]    = "http://www.w3.org/ns/dcat#",
                ["dqv"]     = "http://www.w3.org/ns/dqv#",
                ["prov"]    = "http://www.w3.org/ns/prov#",
                ["ebucore"] = "https://tech-metadata.ebu-it-tools.ch/ontologies/ebucore/",
                ["cr"]      = "http://mlcommons.org/croissant/",
                ["ddpv"]    = "https://datapartnership.org/ddpv-metadata-terms",
                ["rai"]     = "http://mlcommons.org/croissant/RAI/",
                ["olac"]    = "http://www.language-archives.org/OLAC/1.1/",
                ["rdfs"]    = "http://www.w3.org/2000/01/rdf-schema#",
                ["chambo"]  = "https://datapartnership.org/chambo-metadata-terms/",
            }
        };

        var graph = groups.Select(g => (object)BuildDataset(g.Collection, g.Files, baseUrl, transcriptions, taggerTags)).ToList();

        var doc = new Dictionary<string, object?>
        {
            ["@context"]     = context,
            ["generatedAt"]  = DateTime.UtcNow.ToString("o"),
            ["samplingInfo"] = new Dictionary<string, object?>
            {
                ["totalMatched"] = totalMatched,
                ["limit"]        = limit,
                ["returned"]     = returned,
                ["randomSample"] = returned < totalMatched
            },
            ["@graph"] = graph
        };

        return JsonSerializer.Serialize(doc, JsonOpts);
    }

    // ── Dataset node ─────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildDataset(
        Collection c,
        CollectionFile[] files,
        string? baseUrl,
        IReadOnlyDictionary<string, CollectionFile> transcriptions,
        Dictionary<string, List<FileTag>>? taggerTags)
    {
        var isMedia = c.MediaType != MediaType.Text;
        return new Dictionary<string, object?>
        {
            ["@type"]                     = "sc:Dataset",
            ["@id"]                       = c.Id,
            ["sc:identifier"]             = c.Id,
            ["sc:name"]                   = c.Name,
            ["sc:description"]            = c.Description,
            ["ddpv:mediaType"]            = c.MediaType.ToString(),
            ["sc:version"]                = c.Version,
            ["sc:license"]                = c.License,
            ["sc:datePublished"]          = c.DatePublished,
            ["sc:dateModified"]           = c.DateModified,
            ["sc:temporalCoverage"]       = c.TemporalCoverage,
            ["sc:keywords"]               = Split(c.Keywords),
            ["dcat:theme"]                = Split(c.Themes),
            ["ddpv:piiScreening"]         = isMedia ? c.PiiScreening : null,
            ["ddpv:sensitiveContent"]     = isMedia ? c.SensitiveContent : null,
            ["olac:subjectLanguage"]      = c.LanguageCode != null || c.LanguageName != null
                ? new Dictionary<string, string?> { ["name"] = c.LanguageName, ["code"] = c.LanguageCode }
                : null,
            ["sc:creator"]                = Org(c.Creator),
            ["sc:publisher"]              = Org(c.Publisher),
            ["sc:provider"]               = Org(c.Provider),
            ["sc:sourceOrganization"]     = Org(c.SourceOrganization),
            ["cr:isLiveDataset"]          = false,
            ["ddpv:bundleUrl"]            = isMedia && baseUrl != null
                ? $"{baseUrl}/api/collections/{Uri.EscapeDataString(c.Id)}/bundle"
                : null,
            ["dqv:hasQualityMeasurement"] = isMedia ? BuildQualityMeasurements(files) : null,
            ["distribution"]              = files
                .Select(f => f.Kind == FileKind.Text
                    ? BuildTextFileObject(f, baseUrl, taggerTags)
                    : BuildMediaFileObject(f, baseUrl, transcriptions))
                .ToList(),
        };
    }

    private static Dictionary<string, string?>? Org(string? name) =>
        name is null ? null : new() { ["@type"] = "sc:Organization", ["sc:name"] = name };

    private static string[]? Split(string? values) =>
        values?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── Quality measurements ─────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> BuildQualityMeasurements(CollectionFile[] files)
    {
        if (files.Length == 0)
            return [];

        var totalDuration  = files.Sum(f => f.Duration ?? 0);
        var withRate       = files.Where(f => f.SampleRate.HasValue).ToArray();
        var weightedBitrate = WeightedMean(files, f => f.Bitrate);
        var weightedLoudness = WeightedMean(files, f => f.IntegratedLoudness);
        var weightedSnr = WeightedMean(files, f => f.SignalToNoiseRatio);

        var measurements = new List<Dictionary<string, object?>>
        {
            Measurement("ddpv:NumFiles",    files.Length,  "count",   "Total number of media files.",           "count"),
            Measurement("ebucore:duration", totalDuration, "seconds", "Total duration across all media files.", "sum"),
        };

        if (withRate.Length > 0)
            measurements.Add(Measurement("ebucore:sampleRate", withRate.Average(f => f.SampleRate!.Value), "Hz", "Mean sample rate across all files.", "mean"));
        if (weightedBitrate.HasValue)
            measurements.Add(Measurement("ebucore:bitrate", weightedBitrate.Value, "bps", "Duration-weighted mean bitrate.", "weighted_mean"));
        if (weightedLoudness.HasValue)
            measurements.Add(Measurement("ebucore:loudnessLUFS", weightedLoudness.Value, "LUFS", "Duration-weighted mean integrated loudness (EBU R128).", "weighted_mean"));
        if (weightedSnr.HasValue)
            measurements.Add(Measurement("ebucore:signalToNoiseRatio", weightedSnr.Value, "dB", "Duration-weighted mean SNR estimate.", "weighted_mean"));

        return measurements;
    }

    private static double? WeightedMean(CollectionFile[] files, Func<CollectionFile, double?> value)
    {
        var withValue = files.Where(f => value(f).HasValue).ToArray();
        if (withValue.Length == 0)
            return null;

        var duration = withValue.Sum(f => f.Duration ?? 0);
        return duration > 0
            ? withValue.Sum(f => value(f)!.Value * (f.Duration ?? 0)) / duration
            : withValue.Average(f => value(f)!.Value);
    }

    private static Dictionary<string, object?> Measurement(
        string metric, double value, string unit, string description, string aggregationType) =>
        new()
        {
            ["@type"]                = "dqv:QualityMeasurement",
            ["dqv:isMeasurementOf"]  = metric,
            ["dqv:value"]            = value,
            ["schema:unitText"]      = unit,
            ["schema:description"]   = description,
            ["ddpv:aggregationType"] = aggregationType,
        };

    // ── File objects ─────────────────────────────────────────────────────────

    private static string ContentUrl(CollectionFile f, string? baseUrl) =>
        baseUrl != null
            ? $"{baseUrl}/api/files/{Uri.EscapeDataString(f.Id)}"
            : f.ContentUrl;

    private static Dictionary<string, object?> BuildTextFileObject(
        CollectionFile f, string? baseUrl, Dictionary<string, List<FileTag>>? taggerTags) =>
        new()
        {
            ["@type"]              = "cr:FileObject",
            ["@id"]                = f.Id,
            ["sc:name"]            = f.Name,
            ["sc:contentUrl"]      = ContentUrl(f, baseUrl),
            ["sc:encodingFormat"]  = f.EncodingFormat,
            ["sc:contentSize"]     = f.ContentSize,
            ["cr:sha256"]          = f.Sha256,
            ["sc:author"]          = f.Author,
            ["sc:datePublished"]   = f.DatePublished,
            ["sc:dateModified"]    = f.DateModified,
            ["sc:numberOfPages"]   = f.NumberOfPages,
            ["sc:wordCount"]       = f.WordCount,
            ["ddpv:tokenCount"]    = f.TokenCount,
            ["ddpv:charCount"]     = f.CharCount,
            ["sc:keywords"]        = f.Keywords.Select(k => k.Value).ToList(),
            ["dcat:theme"]         = f.Themes.Select(t => t.Value).ToList(),
            ["dcat:themeTaxonomy"] = "https://vocabularyserver.com/unbis/en/index.php",
            ["chambo:tags"]        = BuildTaggerTags(f.ContentUrl, taggerTags)
        };

    private static Dictionary<string, object?> BuildMediaFileObject(
        CollectionFile f, string? baseUrl, IReadOnlyDictionary<string, CollectionFile> transcriptions)
    {
        var file = new Dictionary<string, object?>
        {
            ["@type"]                      = "cr:FileObject",
            ["@id"]                        = f.Id,
            ["sc:name"]                    = f.Name,
            ["sc:contentUrl"]              = ContentUrl(f, baseUrl),
            ["sc:encodingFormat"]          = f.EncodingFormat,
            ["sc:contentSize"]             = f.ContentSize,
            ["sc:dateModified"]            = f.DateModified,
            ["cr:sha256"]                  = f.Sha256,
            ["ebucore:duration"]           = f.Duration,
            ["ebucore:sampleRate"]         = f.SampleRate,
            ["ebucore:channels"]           = f.Channels,
            ["ebucore:bitrate"]            = f.Bitrate,
            ["ebucore:integratedLoudness"] = f.IntegratedLoudness,
            ["ebucore:signalToNoiseRatio"] = f.SignalToNoiseRatio,
            ["sc:author"]                  = f.Author,
            ["sc:keywords"]                = NullIfEmpty(f.Keywords.Select(k => k.Value)),
            ["dcat:theme"]                 = NullIfEmpty(f.Themes.Select(t => t.Value)),
        };

        if (f.TranscriptionFileId != null && transcriptions.TryGetValue(f.TranscriptionFileId, out var t))
        {
            file["ddpv:transcription"] = new Dictionary<string, object?>
            {
                ["@type"]             = "cr:FileObject",
                ["@id"]               = t.Id,
                ["sc:name"]           = t.Name,
                ["sc:contentUrl"]     = ContentUrl(t, baseUrl),
                ["sc:encodingFormat"] = t.EncodingFormat,
                ["sc:contentSize"]    = t.ContentSize,
                ["sc:dateModified"]   = t.DateModified,
                ["cr:sha256"]         = t.Sha256,
            };
            if (baseUrl != null)
                file["ddpv:bundleUrl"] = $"{baseUrl}/api/files/{Uri.EscapeDataString(f.Id)}/bundle";
        }

        return file;
    }

    private static List<string>? NullIfEmpty(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list;
    }

    private static List<Dictionary<string, string>>? BuildTaggerTags(
        string contentUrl,
        Dictionary<string, List<FileTag>>? taggerTags)
    {
        if (taggerTags is null || !taggerTags.TryGetValue(contentUrl, out var tags) || tags.Count == 0)
            return null;

        return tags
            .Select(t => new Dictionary<string, string>
            {
                ["chambo:tagKey"]   = t.TagKey,
                ["chambo:tagValue"] = t.TagValue
            })
            .ToList();
    }
}
