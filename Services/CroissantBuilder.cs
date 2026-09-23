using System.Text.Json;
using System.Text.Json.Serialization;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Builds a Croissant 1.0 JSON-LD document from a list of FileRecords.
/// Files are grouped by their parent Dataset so each year becomes one
/// sc:Dataset node inside @graph.
/// </summary>
public class CroissantBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Build(
        List<FileRecord> files,
        int totalMatched,
        int limit,
        string? baseUrl = null,
        Dictionary<string, List<FileTag>>? taggerTags = null)
    {
        var grouped = files.GroupBy(f => f.Dataset!).ToList();

        // ── @context ────────────────────────────────────────────────────────
        var context = new object[]
        {
            "https://mlcommons.org/working-groups/data/croissant/",
            new Dictionary<string, string>
            {
                ["sc"]     = "https://schema.org/",
                ["dct"]    = "http://purl.org/dc/terms/",
                ["dqv"]    = "http://www.w3.org/ns/dqv#",
                ["cr"]     = "http://mlcommons.org/croissant/",
                ["ddpv"]   = "https://datapartnership.org/ddpv-metadata-terms",
                ["olac"]   = "http://www.language-archives.org/OLAC/1.1/",
                ["dcat"]   = "http://www.w3.org/ns/dcat#",
                ["prov"]   = "http://www.w3.org/ns/prov#",
                ["rai"]    = "http://mlcommons.org/croissant/RAI/",
                ["rdfs"]   = "http://www.w3.org/2000/01/rdf-schema#",
                ["chambo"] = "https://datapartnership.org/chambo-metadata-terms/"
            }
        };

        // ── @graph – one sc:Dataset node per year ───────────────────────────
        var graph = grouped.Select(g =>
        {
            var ds = g.Key;
            return (object)new Dictionary<string, object?>
            {
                ["@type"]                = "sc:Dataset",
                ["@id"]                  = ds.Id,
                ["sc:name"]              = ds.Name,
                ["sc:temporalCoverage"]  = ds.TemporalCoverage,
                ["sc:spatialCoverage"]   = ds.SpatialCoverage,
                ["sc:inLanguage"]        = ds.LanguageCode,
                ["sc:creator"]           = new Dictionary<string, object?>
                {
                    ["@type"]   = "sc:Organization",
                    ["sc:name"] = ds.Creator
                },
                ["sc:publisher"]         = new Dictionary<string, object?>
                {
                    ["@type"]   = "sc:Organization",
                    ["sc:name"] = ds.Publisher
                },
                ["sc:license"]           = ds.License,
                ["sc:version"]           = ds.Version,
                ["cr:isLiveDataset"]     = false,
                ["distribution"]         = g.Select(f => BuildFileObject(f, baseUrl, taggerTags)).ToList()
            };
        }).ToList();

        // ── Wrapper with sampling metadata ──────────────────────────────────
        var doc = new Dictionary<string, object?>
        {
            ["@context"]     = context,
            ["generatedAt"]  = DateTime.UtcNow.ToString("o"),
            ["samplingInfo"] = new Dictionary<string, object?>
            {
                ["totalMatched"] = totalMatched,
                ["limit"]        = limit,
                ["returned"]     = files.Count,
                ["randomSample"] = files.Count < totalMatched
            },
            ["@graph"] = graph
        };

        return JsonSerializer.Serialize(doc, JsonOpts);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildFileObject(
        FileRecord f,
        string? baseUrl,
        Dictionary<string, List<FileTag>>? taggerTags) =>
        new()
        {
            ["@type"]               = "cr:FileObject",
            ["@id"]                 = f.Id,
            ["sc:name"]             = f.Name,
            ["sc:contentUrl"]       = baseUrl != null
                                        ? $"{baseUrl}/api/files/{Uri.EscapeDataString(f.Id)}"
                                        : f.ContentUrl,
            ["sc:encodingFormat"]   = f.EncodingFormat,
            ["sc:contentSize"]      = f.ContentSize,
            ["cr:sha256"]           = f.Sha256,
            ["sc:author"]           = f.Author,
            ["sc:datePublished"]    = f.DatePublished,
            ["sc:dateModified"]     = f.DateModified,
            ["sc:inLanguage"]       = f.LanguageCode,
            ["sc:numberOfPages"]    = f.NumberOfPages,
            ["sc:wordCount"]        = f.WordCount,
            ["ddpv:tokenCount"]     = f.TokenCount,
            ["ddpv:charCount"]      = f.CharCount,
            ["dct:subject"]         = f.Subject,
            ["olac:discourseType"]  = f.DiscourseType,
            ["sc:keywords"]         = f.Keywords.Select(k => k.Value).ToList(),
            ["dcat:theme"]          = f.Themes.Select(t => t.Value).ToList(),
            ["dcat:themeTaxonomy"]  = "https://vocabularyserver.com/unbis/en/index.php",
            ["chambo:tags"]         = BuildTaggerTags(f.ContentUrl, taggerTags)
        };

    private static List<Dictionary<string, string>>? BuildTaggerTags(
        string? contentUrl,
        Dictionary<string, List<FileTag>>? taggerTags)
    {
        if (taggerTags is null || string.IsNullOrEmpty(contentUrl))
            return null;

        if (!taggerTags.TryGetValue(contentUrl, out var tags) || tags.Count == 0)
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
