namespace MetadataTagging.Models;

// Ported from the Low Resource Language Library API (LRLL) merge: text dataset/file
// catalog entities that back the Croissant JSON-LD dataset/file query endpoints.

public class Dataset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TemporalCoverage { get; set; }
    public string SpatialCoverage { get; set; } = "Malawi";
    public string LanguageCode { get; set; } = "ny";
    public string Creator { get; set; } = "Nation Publications Limited";
    public string Publisher { get; set; } = "Nation Publications Limited";
    public string License { get; set; } = "LicenseRef-InternalDemoOnly";
    public string Version { get; set; } = "v1.0.0";
    public string? DatePublished { get; set; }
    public string? DateModified { get; set; }

    public ICollection<FileRecord> Files { get; set; } = [];
}

public class FileRecord
{
    public string Id { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ContentUrl { get; set; }
    public string EncodingFormat { get; set; } = "application/pdf";
    public long? ContentSize { get; set; }
    public string? Sha256 { get; set; }
    public string? Author { get; set; }
    public string? DatePublished { get; set; }
    public string? DateModified { get; set; }
    public int? WordCount { get; set; }
    public int? CharCount { get; set; }
    public int? TokenCount { get; set; }
    public int NumberOfPages { get; set; } = 1;
    public string LanguageCode { get; set; } = "ny";
    public string Subject { get; set; } = "news";
    public string DiscourseType { get; set; } = "news article";

    public Dataset? Dataset { get; set; }
    public ICollection<Keyword> Keywords { get; set; } = [];
    public ICollection<Theme> Themes { get; set; } = [];
}

public class Keyword
{
    public int Id { get; set; }
    public string FileId { get; set; } = "";
    public string Value { get; set; } = "";
    public FileRecord? File { get; set; }
}

public class Theme
{
    public int Id { get; set; }
    public string FileId { get; set; } = "";
    public string Value { get; set; } = "";
    public FileRecord? File { get; set; }
}

/// <summary>
/// Tracks how many files a user has downloaded on a given UTC day.
/// Composite primary key: (UserId, Date).
/// </summary>
public class FileDownloadLog
{
    /// <summary>Local application user id (string-formatted), from <c>ClaimTypes.NameIdentifier</c>.</summary>
    public string UserId { get; set; } = "";

    /// <summary>UTC calendar date of the downloads.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Number of files downloaded by this user on this day.</summary>
    public int Count { get; set; }
}

public class DatasetAccessApplication
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Organization { get; set; } = "";
    public string OrganizationEmail { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string ProjectDescription { get; set; } = "";
    public string EthicalDataHandling { get; set; } = "";
    public bool OpenSourceCommitment { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}
