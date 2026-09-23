namespace MetadataTagging.DTOs;

/// <summary>Controls how the seed endpoint populates the database.</summary>
public enum SeedMode
{
    /// <summary>Scan the configured file storage and ingest real file metadata.</summary>
    Scan = 0,
    /// <summary>Generate synthetic records without touching the file storage.</summary>
    Dummy = 1,
}

/// <summary>Request body for <c>POST /api/seed</c>.</summary>
public class SeedRequest
{
    /// <summary>
    /// Whether to scan real storage or generate dummy records.
    /// Default: <c>Scan</c>.
    /// </summary>
    /// <example>Scan</example>
    public SeedMode Mode { get; set; } = SeedMode.Scan;

    /// <summary>
    /// Comma-separated media-house names used in dummy mode only.
    /// Defaults to <c>MediaHouseA,MediaHouseB,MediaHouseC,MediaHouseD</c>.
    /// </summary>
    /// <example>MediaHouseA,MediaHouseB</example>
    public string? DummyHouses { get; set; }

    /// <summary>Number of synthetic file records per media house (dummy mode only). Default: 40.</summary>
    /// <example>40</example>
    public int DummyFilesPerHouse { get; set; } = 40;
}

/// <summary>Summary returned after a successful seed operation.</summary>
public class SeedResult
{
    /// <summary>Number of new Dataset rows created.</summary>
    public int DatasetsCreated { get; set; }

    /// <summary>Number of new FileRecord rows inserted.</summary>
    public int FilesInserted { get; set; }

    /// <summary>Files already present in the database that were skipped (scan mode only).</summary>
    public int FilesSkipped { get; set; }

    /// <summary>Number of inserted files whose metadata (themes, keywords, author, title) came from the tagger database.</summary>
    public int TaggerFilesMatched { get; set; }

    /// <summary>Non-null when the tagger database could not be reached or queried; seeded files will have empty metadata.</summary>
    public string? TaggerError { get; set; }

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Summary returned after a successful sound seed operation.</summary>
public class SoundSeedResult
{
    /// <summary>Number of new SoundDataset rows created.</summary>
    public int DatasetsAdded { get; set; }

    /// <summary>Number of new SoundFileRecord rows inserted.</summary>
    public int FilesAdded { get; set; }

    /// <summary>Sound files already present in the database that were skipped.</summary>
    public int FilesSkipped { get; set; }

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Summary returned after resetting API-managed database tables.</summary>
public class ResetDbResult
{
    /// <summary>Number of Dataset rows deleted.</summary>
    public int DatasetsDeleted { get; set; }

    /// <summary>Number of FileRecord rows deleted.</summary>
    public int FilesDeleted { get; set; }

    /// <summary>Number of Keyword rows deleted.</summary>
    public int KeywordsDeleted { get; set; }

    /// <summary>Number of Theme rows deleted.</summary>
    public int ThemesDeleted { get; set; }

    /// <summary>Number of SoundDataset rows deleted.</summary>
    public int SoundDatasetsDeleted { get; set; }

    /// <summary>Number of SoundFileRecord rows deleted.</summary>
    public int SoundFilesDeleted { get; set; }

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; set; } = string.Empty;
}
