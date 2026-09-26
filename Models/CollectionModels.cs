namespace MetadataTagging.Models;

/// <summary>Top-level media folder under each provider in catalog storage.</summary>
public enum MediaType
{
    Text,
    Audio,
    Video,
}

/// <summary>What a <see cref="CollectionFile"/> contains.</summary>
public enum FileKind
{
    Text,
    Audio,
    Video,
    Transcription,
}

/// <summary>
/// One collection folder in catalog storage (<c>Provider/CollectionFolder</c>).
/// Rows are rebuilt by <c>POST /api/seed</c>; <see cref="Id"/> is stable across re-seeds.
/// </summary>
public class Collection
{
    /// <summary>The jsonld <c>sc:identifier</c>, or a hash of <see cref="StoragePrefix"/>.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public MediaType MediaType { get; set; }
    /// <summary>Storage key prefix of the collection folder, e.g. <c>Radio Maria/KUCHEZA</c>.</summary>
    public string StoragePrefix { get; set; } = "";
    public string? Description { get; set; }

    public string? LanguageName { get; set; }
    public string? LanguageCode { get; set; }
    /// <summary>Comma-separated keyword list.</summary>
    public string? Keywords { get; set; }
    /// <summary>Comma-separated theme list.</summary>
    public string? Themes { get; set; }

    public string? TemporalCoverage { get; set; }
    public int? StartYear { get; set; }
    public int? EndYear { get; set; }
    public string? DatePublished { get; set; }
    public string? DateModified { get; set; }

    public string? License { get; set; }
    public string? Creator { get; set; }
    public string? Publisher { get; set; }
    public string? SourceOrganization { get; set; }
    public string? Version { get; set; }
    public bool PiiScreening { get; set; }
    public bool SensitiveContent { get; set; }

    /// <summary>Number of primary (non-transcription) files.</summary>
    public int FileCount { get; set; }
    public double? TotalDurationSeconds { get; set; }
    /// <summary>Comma-separated distinct encoding formats of the primary files.</summary>
    public string? Formats { get; set; }
    public DateTime SeededAtUtc { get; set; }

    public ICollection<CollectionFile> Files { get; set; } = [];
}

public class CollectionFile
{
    /// <summary>Hash of <see cref="ContentUrl"/>.</summary>
    public string Id { get; set; } = "";
    public string CollectionId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Storage key relative to the catalog storage root.</summary>
    public string ContentUrl { get; set; } = "";
    public FileKind Kind { get; set; }
    public string EncodingFormat { get; set; } = "";
    public long? ContentSize { get; set; }
    public string? Sha256 { get; set; }
    public string? DateModified { get; set; }
    /// <summary>From the tagger's Author tag.</summary>
    public string? Author { get; set; }

    // Text only
    public string? DatePublished { get; set; }
    public int? WordCount { get; set; }
    public int? CharCount { get; set; }
    public int? TokenCount { get; set; }
    public int? NumberOfPages { get; set; }

    // Audio / Video only (ebucore)
    public double? Duration { get; set; }
    public double? SampleRate { get; set; }
    public int? Channels { get; set; }
    public double? Bitrate { get; set; }
    public double? IntegratedLoudness { get; set; }
    public double? SignalToNoiseRatio { get; set; }

    /// <summary>For audio/video files, the Id of the paired transcription file (if any).</summary>
    public string? TranscriptionFileId { get; set; }

    public Collection? Collection { get; set; }
    public ICollection<Keyword> Keywords { get; set; } = [];
    public ICollection<Theme> Themes { get; set; } = [];
}
