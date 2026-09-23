namespace MetadataTagging.Models;

// Ported from the LRLL merge: sound (FLAC/audio) dataset/file catalog entities.

public class SoundDataset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? DatePublished { get; set; }
    public string? DateCreated { get; set; }
    public string? DateModified { get; set; }
    public string? TemporalCoverage { get; set; }
    public string? SpatialCoverage { get; set; }
    public string? License { get; set; }

    /// <summary>From olac:subjectLanguage.name</summary>
    public string? SubjectLanguageName { get; set; }
    /// <summary>From olac:subjectLanguage.code (ISO 639)</summary>
    public string? SubjectLanguageCode { get; set; }

    /// <summary>From sc:inLanguage.name</summary>
    public string? InLanguageName { get; set; }
    /// <summary>From sc:inLanguage.identifier (e.g. en-GB)</summary>
    public string? InLanguageIdentifier { get; set; }

    public string? CreatorName { get; set; }
    public string? ProviderName { get; set; }
    public string? SourceOrganizationName { get; set; }

    /// <summary>ddpv:piiScreening</summary>
    public bool PiiScreening { get; set; }
    /// <summary>ddpv:sensitiveContent</summary>
    public bool SensitiveContent { get; set; }

    /// <summary>sc:keywords stored as comma-separated string.</summary>
    public string? Keywords { get; set; }

    public ICollection<SoundFileRecord> Files { get; set; } = [];
}

public class SoundFileRecord
{
    public string Id { get; set; } = "";
    public string SoundDatasetId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Storage key relative to the sound bucket root, used for download.</summary>
    public string ContentUrl { get; set; } = "";

    public string EncodingFormat { get; set; } = "audio/flac";
    public long? ContentSize { get; set; }
    public string? DateModified { get; set; }
    public string? Sha256 { get; set; }

    /// <summary>ebucore:duration in seconds</summary>
    public double Duration { get; set; }
    /// <summary>ebucore:sampleRate in Hz</summary>
    public double SampleRate { get; set; }
    /// <summary>ebucore:channels</summary>
    public int Channels { get; set; }
    /// <summary>ebucore:bitrate in bps</summary>
    public double Bitrate { get; set; }
    /// <summary>ebucore:integratedLoudness in LUFS (EBU R128)</summary>
    public double? IntegratedLoudness { get; set; }
    /// <summary>ebucore:signalToNoiseRatio in dB</summary>
    public double? SignalToNoiseRatio { get; set; }

    public SoundDataset? Dataset { get; set; }
}
