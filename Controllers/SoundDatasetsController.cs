using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Queries the sound (FLAC/audio) catalogue and returns Croissant 1.0 JSON-LD responses.
/// </summary>
[ApiController]
[Route("api/sound")]
[Authorize(Policy = "Read")]
public class SoundDatasetsController(
    SoundQueryService queryService,
    SoundCroissantBuilder builder,
    ApplicationDbContext db) : ControllerBase
{
    /// <summary>Query sound datasets and receive a Croissant 1.0 JSON-LD document.</summary>
    /// <remarks>
    /// Returns audio file metadata grouped by programme/dataset.
    /// Filtered queries return only files matching every supplied filter and are
    /// randomly sampled to <paramref name="p"/>.<c>Limit</c>. With no filters
    /// or limit, the API returns a random 80% sample of all sound files.
    ///
    /// ### Example
    ///     GET /api/sound/query?language=ny&amp;limit=20
    /// </remarks>
    /// <param name="p">Filter and sampling parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Croissant 1.0 JSON-LD document</response>
    /// <response code="400">Invalid limit value</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Insufficient permissions</response>
    [HttpGet("query")]
    [Produces("application/ld+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Query([FromQuery] SoundDatasetQueryParams p, CancellationToken ct)
    {
        if (p.Limit is < 1 or > 100)
            return BadRequest(new { error = "Limit must be between 1 and 100." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var (groups, totalMatched, appliedLimit) = await queryService.QueryAsync(p);

        if (groups.Count == 0)
            return Ok(new NoMatchResponse { message = "No sound files match the given filters." });

        var json = builder.Build(groups, totalMatched, appliedLimit, baseUrl);
        return Content(json, "application/ld+json");
    }

    /// <summary>Returns a simple health check with the total number of sound files in the catalogue.</summary>
    /// <response code="200">Health check response</response>
    [HttpGet("health")]
    [AllowAnonymous]
    [Produces("application/json")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var count = await db.SoundFileRecords.CountAsync(ct);
        return Ok(new { status = "ok", soundFileCount = count });
    }
}
