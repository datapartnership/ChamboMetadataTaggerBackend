using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Public catalog endpoints for the CKAN-style data catalog UI.
/// All endpoints are anonymous — no authentication required.
/// </summary>
[ApiController]
[Route("api/catalog")]
[AllowAnonymous]
public class CatalogController(CatalogService catalogService) : ControllerBase
{
    // ── Text datasets ─────────────────────────────────────────────────────────

    /// <summary>Paginated list of text datasets for the catalog browser.</summary>
    /// <remarks>
    /// Supports full-text search (<c>q</c>) and facet filters (keyword, theme, year).
    ///
    /// ### Example
    ///     GET /api/catalog/datasets?q=health&amp;year=2024&amp;page=1&amp;pageSize=12
    /// </remarks>
    /// <param name="p">Query and pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated list of text datasets</response>
    [HttpGet("datasets")]
    [ProducesResponseType(typeof(CatalogPagedResult<CatalogDatasetListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDatasets([FromQuery] CatalogDatasetQueryParams p, CancellationToken ct)
    {
        var result = await catalogService.ListDatasetsAsync(p, ct);
        return Ok(result);
    }

    /// <summary>Full detail for a single text dataset.</summary>
    /// <param name="id">Dataset identifier, e.g. <c>nation-2024</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Dataset detail with metadata and aggregate counts</response>
    /// <response code="404">Dataset not found</response>
    [HttpGet("datasets/{id}")]
    [ProducesResponseType(typeof(CatalogDatasetDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDataset(string id, CancellationToken ct)
    {
        var result = await catalogService.GetDatasetAsync(id, ct);
        if (result is null)
            return NotFound(new ErrorResponse { error = $"Dataset '{id}' not found." });
        return Ok(result);
    }

    /// <summary>Paginated file-level results for the catalog file list and dataset detail pages.</summary>
    [HttpGet("files")]
    [ProducesResponseType(typeof(CatalogPagedResult<CatalogFileListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiles([FromQuery] CatalogFileQueryParams p, CancellationToken ct)
    {
        var result = await catalogService.ListFilesAsync(p, ct);
        return Ok(result);
    }

    // ── Sound datasets ────────────────────────────────────────────────────────

    /// <summary>Paginated list of sound datasets for the catalog browser.</summary>
    /// <remarks>
    /// ### Example
    ///     GET /api/catalog/sound/datasets?language=ny&amp;page=1&amp;pageSize=12
    /// </remarks>
    /// <param name="p">Query and pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated list of sound datasets</response>
    [HttpGet("sound/datasets")]
    [ProducesResponseType(typeof(CatalogPagedResult<CatalogSoundDatasetListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSoundDatasets([FromQuery] CatalogSoundQueryParams p, CancellationToken ct)
    {
        var result = await catalogService.ListSoundDatasetsAsync(p, ct);
        return Ok(result);
    }

    /// <summary>Full detail for a single sound dataset.</summary>
    /// <param name="id">Sound dataset identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Sound dataset detail with all audio files</response>
    /// <response code="404">Sound dataset not found</response>
    [HttpGet("sound/datasets/{id}")]
    [ProducesResponseType(typeof(CatalogSoundDatasetDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSoundDataset(string id, CancellationToken ct)
    {
        var result = await catalogService.GetSoundDatasetAsync(id, ct);
        if (result is null)
            return NotFound(new ErrorResponse { error = $"Sound dataset '{id}' not found." });
        return Ok(result);
    }

    // ── Facets ────────────────────────────────────────────────────────────────

    /// <summary>Returns top keyword, theme, year, and language facets for the catalog sidebar.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Facet values with counts</response>
    [HttpGet("facets")]
    [ProducesResponseType(typeof(CatalogFacetsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacets(CancellationToken ct)
    {
        var result = await catalogService.GetFacetsAsync(ct);
        return Ok(result);
    }
}
