using System.ComponentModel.DataAnnotations;

namespace MetadataTagging.DTOs;

public class CreateDatasetAccessApplicationRequest
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = "";

    [Required, StringLength(100)]
    public string LastName { get; set; } = "";

    [Required, StringLength(200)]
    public string Organization { get; set; } = "";

    [Required, StringLength(254), EmailAddress]
    public string OrganizationEmail { get; set; } = "";

    [Required, StringLength(200)]
    public string JobTitle { get; set; } = "";

    [Required, StringLength(5000)]
    public string ProjectDescription { get; set; } = "";

    [Required, StringLength(5000)]
    public string EthicalDataHandling { get; set; } = "";

    public bool OpenSourceCommitment { get; set; }
}

public class DatasetAccessApplicationResponse
{
    public int Id { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}

public class DatasetAccessApplicationListItem : DatasetAccessApplicationResponse
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Organization { get; set; } = "";
    public string OrganizationEmail { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string ProjectDescription { get; set; } = "";
    public string EthicalDataHandling { get; set; } = "";
    public bool OpenSourceCommitment { get; set; }
}

public class DatasetAccessApplicationListQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
