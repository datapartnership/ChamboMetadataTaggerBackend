using System.Text.Json;
using System.Text.Json.Serialization;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Builds a Croissant 1.0 JSON-LD document from a collection of
/// <see cref="SoundDataset"/> / <see cref="SoundFileRecord"/> groups.
/// Includes EBU Core audio metadata and DQV quality measurements.
/// </summary>
public class SoundCroissantBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Build(
        List<(SoundDataset Dataset, SoundFileRecord[] Files)> groups,
        int totalMatched,
        int limit,
        string? baseUrl = null)
    {
        var allFiles = groups.SelectMany(g => g.Files).ToList();

        // ── @context ────────────────────────────────────────────────────────
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
            }
        };

        // ── @graph – one sc:Dataset node per SoundDataset ───────────────────
        var graph = groups.Select(g =>
        {
            var ds    = g.Dataset;
            var files = g.Files;

            return (object)new Dictionary<string, object?>
            {
                ["@type"]                    = "sc:Dataset",
                ["@id"]                      = ds.Id,
                ["sc:identifier"]            = ds.Id,
                ["sc:name"]                  = ds.Name,
                ["sc:description"]           = ds.Description,
                ["sc:version"]               = ds.Version,
                ["sc:license"]               = ds.License,
                ["sc:datePublished"]         = ds.DatePublished,
                ["sc:dateCreated"]           = ds.DateCreated,
                ["sc:dateModified"]          = ds.DateModified,
                ["sc:temporalCoverage"]      = ds.TemporalCoverage,
                ["sc:spatialCoverage"]       = ds.SpatialCoverage,
                ["sc:keywords"]              = ds.Keywords?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ["ddpv:piiScreening"]        = ds.PiiScreening,
                ["ddpv:sensitiveContent"]    = ds.SensitiveContent,
                ["olac:subjectLanguage"]     = ds.SubjectLanguageCode != null
                    ? new Dictionary<string, string?> { ["name"] = ds.SubjectLanguageName, ["code"] = ds.SubjectLanguageCode }
                    : null,
                ["sc:inLanguage"]            = ds.InLanguageName != null
                    ? new Dictionary<string, string?> { ["@type"] = "sc:Language", ["name"] = ds.InLanguageName, ["identifier"] = ds.InLanguageIdentifier }
                    : null,
                ["sc:creator"]               = ds.CreatorName != null
                    ? new Dictionary<string, string?> { ["@type"] = "sc:Organization", ["sc:name"] = ds.CreatorName }
                    : null,
                ["sc:provider"]              = ds.ProviderName != null
                    ? new Dictionary<string, string?> { ["@type"] = "sc:Organization", ["sc:name"] = ds.ProviderName }
                    : null,
                ["sc:sourceOrganization"]    = ds.SourceOrganizationName != null
                    ? new Dictionary<string, string?> { ["@type"] = "sc:Organization", ["sc:name"] = ds.SourceOrganizationName }
                    : null,
                ["dqv:hasQualityMeasurement"] = BuildQualityMeasurements(files),
                ["distribution"]             = files.Select(f => BuildFileObject(f, baseUrl)).ToList(),
            };
        }).ToList();

        // ── Wrapper with sampling metadata ───────────────────────────────────
        var doc = new Dictionary<string, object?>
        {
            ["@context"]     = context,
            ["generatedAt"]  = DateTime.UtcNow.ToString("o"),
            ["samplingInfo"] = new Dictionary<string, object?>
            {
                ["totalMatched"] = totalMatched,
                ["limit"]        = limit,
                ["returned"]     = allFiles.Count,
                ["randomSample"] = allFiles.Count < totalMatched
            },
            ["@graph"] = graph
        };

        return JsonSerializer.Serialize(doc, JsonOpts);
    }

    // ── Quality measurements ─────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> BuildQualityMeasurements(SoundFileRecord[] files)
    {
        if (files.Length == 0)
            return [];

        var totalDuration  = files.Sum(f => f.Duration);
        var meanSampleRate = files.Average(f => f.SampleRate);
        var weightedBitrate = totalDuration > 0
            ? files.Sum(f => f.Bitrate * f.Duration) / totalDuration
            : files.Average(f => f.Bitrate);

        var filesWithLoudness = files.Where(f => f.IntegratedLoudness.HasValue).ToArray();
        double? weightedLoudness = null;
        if (filesWithLoudness.Length > 0)
        {
            var dur = filesWithLoudness.Sum(f => f.Duration);
            weightedLoudness = dur > 0
                ? filesWithLoudness.Sum(f => f.IntegratedLoudness!.Value * f.Duration) / dur
                : filesWithLoudness.Average(f => f.IntegratedLoudness!.Value);
        }

        var filesWithSnr = files.Where(f => f.SignalToNoiseRatio.HasValue).ToArray();
        double? weightedSnr = null;
        if (filesWithSnr.Length > 0)
        {
            var dur = filesWithSnr.Sum(f => f.Duration);
            weightedSnr = dur > 0
                ? filesWithSnr.Sum(f => f.SignalToNoiseRatio!.Value * f.Duration) / dur
                : filesWithSnr.Average(f => f.SignalToNoiseRatio!.Value);
        }

        var measurements = new List<Dictionary<string, object?>>
        {
            Measurement("ddpv:NumFiles",       (double)files.Length, "count",   "Total number of audio files.",               "count"),
            Measurement("ebucore:duration",    totalDuration,        "seconds", "Total duration across all audio files.",      "sum"),
            Measurement("ebucore:sampleRate",  meanSampleRate,       "Hz",      "Mean sample rate across all files.",          "mean"),
            Measurement("ebucore:bitrate",     weightedBitrate,      "bps",     "Duration-weighted mean bitrate.",             "weighted_mean"),
        };

        if (weightedLoudness.HasValue)
            measurements.Add(Measurement("ebucore:loudnessLUFS",     weightedLoudness.Value, "LUFS", "Duration-weighted mean integrated loudness (EBU R128).", "weighted_mean"));

        if (weightedSnr.HasValue)
            measurements.Add(Measurement("ebucore:signalToNoiseRatio", weightedSnr.Value, "dB", "Duration-weighted mean SNR estimate.", "weighted_mean"));

        return measurements;
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

    private static Dictionary<string, object?> BuildFileObject(SoundFileRecord f, string? baseUrl) =>
        new()
        {
            ["@type"]                      = "cr:FileObject",
            ["@id"]                        = f.Id,
            ["sc:name"]                    = f.Name,
            ["sc:contentUrl"]              = baseUrl != null
                                                ? $"{baseUrl}/api/sound/files/{Uri.EscapeDataString(f.Id)}"
                                                : f.ContentUrl,
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
        };
}
