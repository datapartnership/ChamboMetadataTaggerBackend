using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Queries the collection catalogue (text, audio, and video) and returns
/// Croissant 1.0 JSON-LD documents ready for machine consumption.
/// </summary>
[ApiController]
[Route("api/collections")]
[Authorize(Policy = "Read")]
public class CollectionsController(
    CollectionQueryService queryService,
    CroissantBuilder builder,
    CollectionBundleService bundles,
    SummaryService summaryService,
    ApplicationDbContext db,
    IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Query collection files and receive a Croissant JSON-LD response.</summary>
    /// <remarks>
    /// All filter parameters are optional and combinable. Only primary files (text, audio,
    /// video) are sampled; each sampled audio/video file includes its transcription.
    /// Filtered queries are randomly sampled to **limit**. With no filters or limit, the API
    /// returns a random 80% sample of all files.
    ///
    /// ### Example
    ///     GET /api/collections/query?mediaType=Audio&amp;language=nya&amp;limit=10
    /// </remarks>
    /// <param name="p">Query parameters</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Croissant JSON-LD document (or a no-match message)</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Insufficient permissions</response>
    [HttpGet("query")]
    [Produces("application/ld+json", "application/json")]
    [ProducesResponseType(typeof(CroissantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse),     StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Query([FromQuery] CollectionQueryParams p, CancellationToken ct)
    {
        if (p.Limit is < 1 or > 100)
            return BadRequest(new ErrorResponse { error = "limit must be between 1 and 100." });
        if (!string.IsNullOrWhiteSpace(p.MediaType) && !CatalogService.TryParseEnum<MediaType>(p.MediaType, out _))
            return BadRequest(new ErrorResponse { error = "mediaType must be Text, Audio, or Video." });

        var result = await queryService.QueryAsync(p, ct);
        if (result.Groups.Count == 0)
            return Ok(new NoMatchResponse { message = "No files matched the supplied filters.", totalMatched = 0 });

        var json = builder.Build(result.Groups, result.TotalMatched, result.AppliedLimit, BaseUrl(),
            result.Transcriptions, result.TaggerTags);
        return Content(json, "application/ld+json");
    }

    /// <summary>Download an entire audio or video collection (media + transcriptions) as a ZIP.</summary>
    /// <remarks>
    /// Streams every media file in the collection, each followed by its transcription, plus a
    /// <c>metadata.jsonld</c> Croissant document describing the pairs. Files missing from storage
    /// are skipped and listed in <c>MISSING_FILES.txt</c>. Text collections cannot be bundled;
    /// download their files individually from <c>/api/files/{id}</c>.
    ///
    /// ### Example
    ///     GET /api/collections/MW-NYA-RM-AUD-015/bundle
    /// </remarks>
    /// <param name="id">Collection identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">ZIP archive of the collection</response>
    /// <response code="400">The collection is a text collection</response>
    /// <response code="404">Collection not found</response>
    [HttpGet("{id}/bundle")]
    [Produces("application/zip")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadBundle(string id, CancellationToken ct)
    {
        var mediaType = await db.Collections.Where(c => c.Id == id).Select(c => (MediaType?)c.MediaType).FirstOrDefaultAsync(ct);
        if (mediaType is null)
            return NotFound(new ErrorResponse { error = $"Collection '{id}' not found." });
        if (mediaType == MediaType.Text)
            return BadRequest(new ErrorResponse { error = "Text collections cannot be bundled; download files individually." });

        var bundle = await bundles.GetCollectionBundleAsync(id, BaseUrl(), ct);
        if (bundle is null)
            return NotFound(new ErrorResponse { error = $"Collection '{id}' not found." });

        return new ZipStreamResult(bundle);
    }

    /// <summary>Aggregated summary of all collections and files, broken down per media type.</summary>
    /// <response code="200">Summary statistics</response>
    /// <response code="500">Database error</response>
    [AllowAnonymous]
    [HttpGet("summary")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(CollectionsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        try
        {
            return Ok(await summaryService.GetSummaryAsync(ct));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { error = $"Failed to generate summary: {ex.Message}" });
        }
    }

    /// <summary>Health check — returns API status and the number of cataloged files.</summary>
    /// <response code="200">API is healthy</response>
    [AllowAnonymous]
    [HttpGet("health")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Health(CancellationToken ct) =>
        Ok(new HealthResponse
        {
            status     = "ok",
            timestamp  = DateTime.UtcNow,
            totalFiles = await db.CollectionFiles.CountAsync(f => f.Kind != FileKind.Transcription, ct),
        });

    private string BaseUrl()
    {
        var scheme = env.IsProduction() ? "https" : Request.Scheme;
        return $"{scheme}://{Request.Host}";
    }
}
