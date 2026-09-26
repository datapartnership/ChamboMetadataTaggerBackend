using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Services;
using Npgsql;

namespace MetadataTagging.Controllers;

/// <summary>
/// Rebuilds the collection catalog from catalog storage, and resets the catalog tables.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = "Write")]
public class SeedController(
    SeedService seedService,
    ApplicationDbContext db,
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

    /// <summary>
    /// Reset API-managed metadata tables by deleting all rows.
    /// </summary>
    /// <remarks>
    /// This operation is destructive and intended for development or controlled admin use.
    /// It deletes data from LRLL API-owned tables only and leaves external/shared tables intact.
    ///
    /// To execute, pass <c>?confirm=true</c>.
    ///
    /// ### Example
    ///     POST /api/seed/reset-db?confirm=true
    /// </remarks>
    /// <param name="confirm">Must be <c>true</c> to execute.</param>
    /// <param name="ct">Cancellation token</param>
    /// <response code="200">Database tables were reset</response>
    /// <response code="400">Missing confirmation flag</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Insufficient permissions</response>
    [HttpPost("reset-db")]
    [ProducesResponseType(typeof(ResetDbResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetDatabase([FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new ErrorResponse { error = "This operation is destructive. Re-run with ?confirm=true." });

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var result = new ResetDbResult
        {
            // Delete children first to satisfy FK constraints on all providers.
            KeywordsDeleted = await DeleteTableRowsAsync(["Keywords", "keywords"], ct),
            ThemesDeleted = await DeleteTableRowsAsync(["Themes", "themes"], ct),
            FilesDeleted = await DeleteTableRowsAsync(["CollectionFiles", "collectionfiles"], ct),
            CollectionsDeleted = await DeleteTableRowsAsync(["Collections", "collections"], ct),
        };

        await tx.CommitAsync(ct);

        result.Message =
            $"Reset complete. Deleted {result.CollectionsDeleted} collections, {result.FilesDeleted} files, " +
            $"{result.KeywordsDeleted} keywords, and {result.ThemesDeleted} themes.";

        logger.LogWarning(
            "Database reset performed by {User}. Deleted collections={CollectionsDeleted}, files={FilesDeleted}.",
            User?.Identity?.Name ?? "unknown",
            result.CollectionsDeleted,
            result.FilesDeleted);

        return Ok(result);
    }

    private async Task<int> DeleteTableRowsAsync(string[] tableNameCandidates, CancellationToken ct)
    {
        foreach (var tableName in tableNameCandidates)
        {
            try
            {
                var sql = $"DELETE FROM \"{tableName.Replace("\"", "\"\"")}\"";
                return await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // Try the next candidate table name if the relation does not exist.
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // Try the next candidate table name if the table does not exist.
            }
        }

        logger.LogWarning(
            "Reset skipped a table because none of the candidate names exist: {Candidates}",
            string.Join(", ", tableNameCandidates));

        return 0;
    }
}
