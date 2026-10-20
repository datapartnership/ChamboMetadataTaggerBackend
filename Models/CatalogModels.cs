namespace MetadataTagging.Models;

// Ported from the Low Resource Language Library API (LRLL) merge: per-file tag rows and
// catalog bookkeeping entities. Collections and their files live in CollectionModels.cs.

public class Keyword
{
    public int Id { get; set; }
    public string FileId { get; set; } = "";
    public string Value { get; set; } = "";
    public CollectionFile? File { get; set; }
}

public class Theme
{
    public int Id { get; set; }
    public string FileId { get; set; } = "";
    public string Value { get; set; } = "";
    public CollectionFile? File { get; set; }
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
