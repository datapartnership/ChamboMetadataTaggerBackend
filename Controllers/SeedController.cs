using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Rebuilds the collection catalog from catalog storage.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = "Write")]
public class SeedController(
    SeedService seedService,
    ILogger<SeedController> logger) : ControllerBase
{
    /// <summary>Scan catalog storage and sync the collection catalog.</summary>
    /// <remarks>
    /// Catalog storage is laid out as <c>Provider/CollectionFolder/...</c>. Every collection folder
    /// becomes a collection, whose media type is inferred from its contents:
    ///
    /// - **Audio / Video** — the folder contains a <c>*_metadata.jsonld</c> (Croissant) describing the
    ///   collection, its media files, and their transcriptions (which may be in subfolders).
    /// - **Text** — no metadata file; PDF/TXT/MD files are cataloged and enriched with approved tagger tags.
    ///
    /// The run is a full sync: new collections/files are added, changed ones updated, and ones no
    /// longer in storage deleted. Skipped or inconsistent content is reported in <c>warnings</c>.
    ///
    /// ### Example
    ///     POST /api/seed
    /// </remarks>
    /// <param name="ct">Cancellation token</param>
    /// <response code="200">Seed completed — returns a summary</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Insufficient permissions</response>
    [HttpPost]
    [ProducesResponseType(typeof(SeedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var result = await seedService.SeedAsync(ct);
        logger.LogInformation("Catalog seed by {User}: {Message}", User?.Identity?.Name ?? "unknown", result.Message);
        return Ok(result);
    }
}
