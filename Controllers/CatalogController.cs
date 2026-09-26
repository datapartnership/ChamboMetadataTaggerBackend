using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Public collection catalog for the catalog browser UI. Collections are rebuilt from
/// catalog storage by <c>POST /api/seed</c>. All endpoints are anonymous.
/// </summary>
[ApiController]
[Route("api/catalog/collections")]
[AllowAnonymous]
public class CatalogController(CatalogService catalogService) : ControllerBase
{
    /// <summary>Paginated list of collections for the catalog browser.</summary>
    /// <remarks>
    /// Supports full-text search (<c>q</c>) and facet filters (mediaType, language, provider, theme, year).
    ///
    /// ### Example
    ///     GET /api/catalog/collections?mediaType=Audio&amp;language=nya&amp;page=1&amp;pageSize=12
    /// </remarks>
    /// <param name="p">Query and pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated list of collections</response>
    [HttpGet]
    [ProducesResponseType(typeof(CatalogPagedResult<CollectionCatalogListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CollectionCatalogQueryParams p, CancellationToken ct) =>
        Ok(await catalogService.ListCollectionsAsync(p, ct));

    /// <summary>Returns media type, language, provider, theme, and year facets for the collections sidebar.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Facet values with counts</response>
    [HttpGet("facets")]
    [ProducesResponseType(typeof(CollectionCatalogFacetsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacets(CancellationToken ct) =>
        Ok(await catalogService.GetFacetsAsync(ct));

    /// <summary>Full detail for a single collection.</summary>
    /// <param name="id">Collection identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Collection detail</response>
    /// <response code="404">Collection not found</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CollectionCatalogDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var result = await catalogService.GetCollectionAsync(id, ct);
        if (result is null)
            return NotFound(new ErrorResponse { error = $"Collection '{id}' not found." });
        return Ok(result);
    }

    /// <summary>Paginated files of a single collection.</summary>
    /// <param name="id">Collection identifier.</param>
    /// <param name="p">Search, kind filter, and pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated list of files</response>
    /// <response code="404">Collection not found</response>
    [HttpGet("{id}/files")]
    [ProducesResponseType(typeof(CatalogPagedResult<CollectionFileItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListFiles(string id, [FromQuery] CollectionFileQueryParams p, CancellationToken ct)
    {
        var result = await catalogService.ListCollectionFilesAsync(id, p, ct);
        if (result is null)
            return NotFound(new ErrorResponse { error = $"Collection '{id}' not found." });
        return Ok(result);
    }
}
