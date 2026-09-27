using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Parses the Croissant JSON-LD metadata file (<c>*_metadata.jsonld</c>) that sits in an
/// audio or video collection folder into a <see cref="Collection"/> and its
/// <see cref="CollectionFile"/> rows, pairing media files with their transcriptions.
/// </summary>
public class CroissantMetadataParser
{
    public const string MetadataFileSuffix = "_metadata.jsonld";

    /// <param name="json">Contents of the collection's <c>*_metadata.jsonld</c> file.</param>
    /// <param name="storagePrefix">
    /// Collection folder key, e.g. <c>Radio Maria/KUCHEZA NDI OMVERA</c>. A distribution
    /// entry's <c>sc:contentUrl</c> is used as the storage key only when it lies under this
    /// folder; otherwise the key is rebuilt as <c>{storagePrefix}/{sc:name}</c>.
    /// </param>
    /// <param name="provider">Provider folder name.</param>
    /// <param name="mediaType">Media folder the collection lives in.</param>
    public (Collection Collection, List<CollectionFile> Files) Parse(
        string json, string storagePrefix, string provider, MediaType mediaType)
    {
        storagePrefix = storagePrefix.Trim('/');

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var collection = new Collection
        {
            Id                 = GetString(root, "sc:identifier") ?? CollectionId(storagePrefix),
            Name               = GetString(root, "sc:name") ?? LastSegment(storagePrefix),
            Provider           = provider,
            MediaType          = mediaType,
            StoragePrefix      = storagePrefix,
            Description        = GetString(root, "sc:description"),
            Version            = GetString(root, "sc:version"),
            DatePublished      = GetString(root, "sc:datePublished"),
            DateModified       = GetString(root, "sc:dateModified"),
            TemporalCoverage   = GetString(root, "sc:temporalCoverage"),
            License            = GetNamed(root, "sc:license"),
            Creator            = GetNamed(root, "sc:creator"),
            Publisher          = GetNamed(root, "sc:publisher") ?? GetNamed(root, "sc:provider"),
            SourceOrganization = GetNamed(root, "sc:sourceOrganization"),
            PiiScreening       = GetBool(root, "ddpv:piiScreening"),
            SensitiveContent   = GetBool(root, "ddpv:sensitiveContent"),
            Keywords           = JoinOrNull(GetStringList(root, "sc:keywords")),
            Themes             = JoinOrNull(GetThemes(root)),
        };

        if (root.TryGetProperty("olac:subjectLanguage", out var olac))
        {
            collection.LanguageName = GetString(olac, "name");
            collection.LanguageCode = GetString(olac, "code");
        }
        if (root.TryGetProperty("sc:inLanguage", out var inLang))
        {
            collection.LanguageName ??= inLang.ValueKind == JsonValueKind.String ? inLang.GetString() : GetString(inLang, "name");
            collection.LanguageCode ??= GetString(inLang, "identifier");
        }

        (collection.StartYear, collection.EndYear) = ParseYearRange(
            collection.TemporalCoverage,
            GetString(root, "rai:dataCollectionTimeFrameStart"),
            GetString(root, "rai:dataCollectionTimeFrameEnd"));

        var files = new List<CollectionFile>();
        if (root.TryGetProperty("distribution", out var dist) && dist.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in dist.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var rawUrl = GetString(entry, "sc:contentUrl")?.Replace('\\', '/').TrimStart('/');
                var name   = GetString(entry, "sc:name") ?? (rawUrl is null ? "" : LastSegment(rawUrl));
                if (name.Length == 0)
                    continue;

                var contentKey = ResolveKey(rawUrl, name, storagePrefix);

                var encodingFormat = GetString(entry, "sc:encodingFormat");
                var kind = ResolveKind(contentKey, encodingFormat, mediaType);
                if (kind == FileKind.Transcription && !IsTextFormat(encodingFormat))
                    encodingFormat = "text/plain";

                var isMedia = kind is FileKind.Audio or FileKind.Video;
                files.Add(new CollectionFile
                {
                    Id                 = FileId(contentKey),
                    CollectionId       = collection.Id,
                    Name               = name,
                    ContentUrl         = contentKey,
                    Kind               = kind,
                    EncodingFormat     = encodingFormat ?? DefaultFormat(kind),
                    ContentSize        = GetLong(entry, "sc:contentSize"),
                    DateModified       = GetString(entry, "sc:dateModified"),
                    Sha256             = GetString(entry, "cr:sha256") ?? GetString(entry, "sc:sha256"),
                    Duration           = isMedia ? GetDouble(entry, "ebucore:duration") : null,
                    SampleRate         = isMedia ? GetDouble(entry, "ebucore:sampleRate") : null,
                    Channels           = isMedia ? GetInt(entry, "ebucore:channels") : null,
                    Bitrate            = isMedia ? GetDouble(entry, "ebucore:bitrate") : null,
                    IntegratedLoudness = isMedia ? GetDouble(entry, "ebucore:integratedLoudness") : null,
                    SignalToNoiseRatio = isMedia
                        ? GetDouble(entry, "ebucore:signalToNoiseRatio") ?? GetDouble(entry, "ddpv:snrProxyDb")
                        : null,
                });

                if (isMedia && TranscriptFrom(entry, storagePrefix, collection.Id) is { } transcript)
                    files.Add(transcript);
            }
        }

        PairTranscriptions(files);
        return (collection, files);
    }

    /// <summary>
    /// Storage key for a <c>sc:contentUrl</c>: kept when under <paramref name="storagePrefix"/>,
    /// re-rooted when relative to the provider folder (<c>/CollectionFolder/sub/a.wav</c>),
    /// otherwise rebuilt as <c>{storagePrefix}/{name}</c>.
    /// </summary>
    private static string ResolveKey(string? rawUrl, string name, string storagePrefix)
    {
        if (rawUrl != null && rawUrl.StartsWith(storagePrefix + "/", StringComparison.Ordinal))
            return rawUrl;
        var folder = LastSegment(storagePrefix) + "/";
        if (rawUrl != null && rawUrl.StartsWith(folder, StringComparison.Ordinal))
            return $"{storagePrefix}/{rawUrl[folder.Length..]}";
        return $"{storagePrefix}/{name.TrimStart('/')}";
    }

    /// <summary>The transcription a media entry points at via <c>ebucore:hasTranscript</c>, if any.</summary>
    private static CollectionFile? TranscriptFrom(JsonElement entry, string storagePrefix, string collectionId)
    {
        if (!entry.TryGetProperty("ebucore:hasTranscript", out var transcript))
            return null;
        var rawUrl = GetString(transcript, "sc:contentUrl")?.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(rawUrl))
            return null;

        var contentKey = ResolveKey(rawUrl, LastSegment(rawUrl), storagePrefix);
        var encodingFormat = GetString(transcript, "sc:encodingFormat");
        return new CollectionFile
        {
            Id             = FileId(contentKey),
            CollectionId   = collectionId,
            Name           = GetString(transcript, "sc:name") ?? LastSegment(contentKey),
            ContentUrl     = contentKey,
            Kind           = FileKind.Transcription,
            EncodingFormat = IsTextFormat(encodingFormat) ? encodingFormat! : "text/plain",
            ContentSize    = GetLong(transcript, "sc:contentSize"),
            DateModified   = GetString(transcript, "sc:dateModified"),
        };
    }

    // ── Deterministic IDs ─────────────────────────────────────────────────────

    public static string CollectionId(string storagePrefix) => $"col-{Hash(storagePrefix.Trim('/'))[..12]}";

    public static string FileId(string contentUrl) => $"file-{Hash(contentUrl)[..16]}";

    private static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    // ── Kinds ─────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".mp3", ".wav", ".ogg", ".opus", ".m4a", ".aac", ".wma", ".aif", ".aiff"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".mpg", ".mpeg", ".wmv", ".ts"
    };

    private static bool IsMediaExtension(string ext) => AudioExtensions.Contains(ext) || VideoExtensions.Contains(ext);

    private static bool IsTextFormat(string? encodingFormat) =>
        encodingFormat?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true;

    internal static FileKind ResolveKind(string contentKey, string? encodingFormat, MediaType mediaType)
    {
        var ext = Path.GetExtension(contentKey);
        if (IsTextFormat(encodingFormat) || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return FileKind.Transcription;
        if (encodingFormat?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true || VideoExtensions.Contains(ext))
            return FileKind.Video;
        if (encodingFormat?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true || AudioExtensions.Contains(ext))
            return FileKind.Audio;
        return mediaType == MediaType.Video ? FileKind.Video : FileKind.Audio;
    }

    private static string DefaultFormat(FileKind kind) => kind switch
    {
        FileKind.Video => "video/mp4",
        FileKind.Transcription => "text/plain",
        _ => "audio/flac",
    };

    // ── Transcription pairing ─────────────────────────────────────────────────

    /// <summary>
    /// Links each audio/video file to the transcription with the same file stem
    /// (<c>clip01.flac</c> ↔ <c>clip01.txt</c> or <c>clip01.flac.txt</c>).
    /// A transcription in the same folder as the media wins; otherwise a stem match
    /// anywhere in the collection is used when it is unambiguous.
    /// </summary>
    internal static void PairTranscriptions(IReadOnlyList<CollectionFile> files)
    {
        var transcriptions = files.Where(f => f.Kind == FileKind.Transcription).ToList();
        if (transcriptions.Count == 0)
            return;

        var byPath = new Dictionary<string, CollectionFile>(StringComparer.OrdinalIgnoreCase);
        var byStem = new Dictionary<string, List<CollectionFile>>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in transcriptions)
        {
            var dir  = FolderOf(t.ContentUrl);
            var stem = Path.GetFileNameWithoutExtension(t.ContentUrl);
            if (IsMediaExtension(Path.GetExtension(stem)))
                stem = Path.GetFileNameWithoutExtension(stem);

            byPath.TryAdd($"{dir}/{stem}", t);
            if (!byStem.TryGetValue(stem, out var list))
                byStem[stem] = list = [];
            list.Add(t);
        }

        foreach (var media in files.Where(f => f.Kind is FileKind.Audio or FileKind.Video))
        {
            var stem = Path.GetFileNameWithoutExtension(media.ContentUrl);
            if (byPath.TryGetValue($"{FolderOf(media.ContentUrl)}/{stem}", out var sameFolder))
                media.TranscriptionFileId = sameFolder.Id;
            else if (byStem.TryGetValue(stem, out var matches) && matches.Count == 1)
                media.TranscriptionFileId = matches[0].Id;
        }
    }

    private static string FolderOf(string key) =>
        Path.GetDirectoryName(key)?.Replace('\\', '/') ?? "";

    private static string LastSegment(string key) =>
        key.TrimEnd('/').Split('/').Last();

    // ── Years ─────────────────────────────────────────────────────────────────

    /// <summary>Extracts start/end years from <c>sc:temporalCoverage</c> (e.g. <c>2013-01-01/2013-12-31</c>
    /// or <c>2013</c>), falling back to the RAI collection time frame.</summary>
    internal static (int? Start, int? End) ParseYearRange(string? temporalCoverage, string? frameStart, string? frameEnd)
    {
        string? start = frameStart, end = frameEnd;
        if (!string.IsNullOrWhiteSpace(temporalCoverage))
        {
            var parts = temporalCoverage.Split('/', 2, StringSplitOptions.TrimEntries);
            start = parts[0];
            end   = parts.Length > 1 ? parts[1] : parts[0];
        }
        return (Year(start), Year(end));

        static int? Year(string? value) =>
            value is { Length: >= 4 } && int.TryParse(value[..4], out var y) ? y : null;
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static string? JoinOrNull(List<string> values) =>
        values.Count == 0 ? null : string.Join(",", values);

    /// <summary>Theme labels from <c>dcat:theme</c>: each scheme's <c>skos:prefLabel</c> and its top concepts'.</summary>
    private static List<string> GetThemes(JsonElement root)
    {
        var themes = new List<string>();
        if (!root.TryGetProperty("dcat:theme", out var theme))
            return themes;

        foreach (var item in theme.ValueKind == JsonValueKind.Array ? theme.EnumerateArray() : Enumerable.Repeat(theme, 1))
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                AddTheme(themes, item.GetString());
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            AddTheme(themes, GetString(item, "skos:prefLabel"));
            if (item.TryGetProperty("skos:hasTopConcept", out var concepts) && concepts.ValueKind == JsonValueKind.Array)
                foreach (var concept in concepts.EnumerateArray())
                    AddTheme(themes, GetString(concept, "skos:prefLabel"));
        }
        return themes;
    }

    private static void AddTheme(List<string> themes, string? value)
    {
        value = value?.Replace(",", " ").Trim();
        if (!string.IsNullOrEmpty(value) && !themes.Contains(value, StringComparer.OrdinalIgnoreCase))
            themes.Add(value);
    }

    private static List<string> GetStringList(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
            return [];
        return v.ValueKind switch
        {
            JsonValueKind.Array  => [.. v.EnumerateArray()
                                        .Where(e => e.ValueKind == JsonValueKind.String)
                                        .Select(e => e.GetString()!.Trim())
                                        .Where(s => s.Length > 0)],
            JsonValueKind.String => [.. v.GetString()!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            _                    => [],
        };
    }

    /// <summary>A plain string, or the <c>sc:name</c>/<c>name</c> of an object value.</summary>
    private static string? GetNamed(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Object => GetString(v, "sc:name") ?? GetString(v, "name"),
            _                    => null,
        };
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool GetBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static double? GetDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static int? GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static long? GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;
}
