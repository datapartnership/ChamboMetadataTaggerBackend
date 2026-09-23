using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Queries the Nation newspaper dataset catalogue and returns
/// Croissant 1.0 JSON-LD documents ready for machine consumption.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/ld+json", "application/json")]
[Authorize(Policy = "Read")]
public class DatasetsController(FileQueryService queryService, SummaryService summaryService, CroissantBuilder builder, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Query files and receive a Croissant JSON-LD response.</summary>
    /// <remarks>
    /// All filter parameters are optional and combinable.
    /// Filtered queries return only files matching every supplied filter and are
    /// randomly sampled to **limit**. With no filters or limit, the API returns
    /// a random 80% sample of all files.
    ///
    /// **Authentication**: supply your API key in the `X-Api-Key` header
    /// (or use the Authorize button in Swagger UI).
    ///
    /// ### Example
    ///     GET /api/datasets/query?keyword=malaria&amp;year=2024&amp;limit=10
    /// </remarks>
    /// <param name="p">Query parameters</param>
    /// <response code="200">Croissant JSON-LD document (or a no-match message)</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="401">Missing API key</response>
    /// <response code="403">Invalid API key</response>
    [HttpGet("query")]
    [ProducesResponseType(typeof(CroissantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse),     StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Query([FromQuery] DatasetQueryParams p)
    {
        if (p.Limit is < 1 or > 100)
            return BadRequest(new { error = "limit must be between 1 and 100." });

        var (files, total, appliedLimit, taggerTags) = await queryService.QueryAsync(p);

        if (files.Count == 0)
            return Ok(new { message = "No files matched the supplied filters.", totalMatched = 0 });

        var scheme  = env.IsProduction() ? "https" : Request.Scheme;
        var baseUrl = $"{scheme}://{Request.Host}";
        var json = builder.Build(files, total, appliedLimit, baseUrl, taggerTags);
        return Content(json, "application/ld+json");
    }

    /// <summary>Health check — returns API status and database file count.</summary>
    /// <response code="200">API is healthy</response>
    [AllowAnonymous]
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Health()
    {
        var (_, total, _, _) = await queryService.QueryAsync(new DatasetQueryParams { Limit = 1 });
        return Ok(new
        {
            status    = "ok",
            timestamp = DateTime.UtcNow,
            totalFiles = total
        });
    }

    /// <summary>Aggregated summary of all datasets and files across text and sound pipelines.</summary>
    /// <remarks>
    /// Returns high-level statistics including:
    /// - Total count of datasets, files, and tagged files
    /// - Top publishers/creators
    /// - Top 20 keywords and themes
    /// - Separate breakdowns for text (Nation newspaper) and sound pipelines
    ///
    /// **Authentication**: Not required (public endpoint).
    /// </remarks>
    /// <response code="200">Summary statistics</response>
    /// <response code="500">Database error</response>
    [AllowAnonymous]
    [HttpGet("summary")]
    [ProducesResponseType(typeof(DatasetsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await summaryService.GetDatasetsSummaryAsync(cancellationToken);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { error = $"Failed to generate summary: {ex.Message}" });
        }
    }
}
