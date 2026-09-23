using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Read-only catalog of data collection efforts, rebuilt from an Excel workbook stored in
/// blob storage (see <see cref="LlrpApi.Options.CollectionCatalogOptions"/>). The workbook
/// is re-synced automatically on app startup; use <see cref="Sync"/> to trigger a re-sync
/// without restarting the app.
/// </summary>
[ApiController]
[Route("api/catalog/collections")]
public class CollectionCatalogController(CollectionCatalogService collectionCatalogService) : ControllerBase
{
    /// <summary>Paginated list of collections for the catalog browser.</summary>
    /// <remarks>
    /// Supports full-text search (<c>q</c>) and facet filters (type, language, provider,
    /// theme, collected, processing, availability, year).
    ///
    /// ### Example
    ///     GET /api/catalog/collections?type=Audio&amp;collected=Yes&amp;page=1&amp;pageSize=12
    /// </remarks>
    /// <param name="p">Query and pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated list of collections</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CatalogPagedResult<CollectionCatalogListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CollectionCatalogQueryParams p, CancellationToken ct)
    {
        var result = await collectionCatalogService.ListAsync(p, ct);
        return Ok(result);
    }

    /// <summary>Full detail for a single collection.</summary>
    /// <param name="id">Collection identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Collection detail</response>
    /// <response code="404">Collection not found</response>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CollectionCatalogDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await collectionCatalogService.GetAsync(id, ct);
        if (result is null)
            return NotFound(new ErrorResponse { error = $"Collection '{id}' not found." });
        return Ok(result);
    }

    /// <summary>Returns type, language, provider, theme, and status facets for the collections sidebar.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Facet values with counts</response>
    [HttpGet("facets")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CollectionCatalogFacetsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacets(CancellationToken ct)
    {
        var result = await collectionCatalogService.GetFacetsAsync(ct);
        return Ok(result);
    }

    /// <summary>Re-syncs the collections catalog from the configured Excel workbook, without
    /// requiring an app restart. Fully replaces the existing catalog table.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Sync completed — returns a summary</response>
    /// <response code="401">Missing credentials</response>
    /// <response code="403">Insufficient permissions</response>
    /// <response code="502">Sync failed (e.g. workbook unreachable or malformed)</response>
    [HttpPost("sync")]
    [Authorize(Policy = "Write")]
    [ProducesResponseType(typeof(CollectionCatalogSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CollectionCatalogSyncResult), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var result = await collectionCatalogService.SyncFromBlobAsync(ct);
        if (!result.success)
            return StatusCode(StatusCodes.Status502BadGateway, result);
        return Ok(result);
    }
}
