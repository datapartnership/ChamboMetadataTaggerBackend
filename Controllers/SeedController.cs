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
/// Populates the database with file metadata by scanning the configured storage
/// (local filesystem or S3 bucket) or by generating synthetic dummy records.
/// Mirrors the functionality of <c>seed_metadata.py</c>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = "Write")]
public class SeedController(
    SeedService seedService,
    SoundSeedService soundSeedService,
    ApplicationDbContext db,
    ILogger<SeedController> logger) : ControllerBase
{
    /// <summary>Scan storage (or generate dummy data) and seed the database.</summary>
    /// <remarks>
    /// Two modes are supported:
    ///
    /// **Scan** (default) — walks the configured storage root.  Each top-level
    /// directory becomes a Dataset; all PDF/TXT files beneath it are inserted as
    /// FileRecords with random UNBIS themes and keywords.  Already-present records
    /// (matched by <c>ContentUrl</c>) are skipped, so the operation is idempotent.
    ///
    /// **Dummy** — generates synthetic records without touching the file storage.
    /// Useful for local development when no real files exist.
    ///
    /// **Authentication**: supply your API key in the `X-Api-Key` header.
    ///
    /// ### Scan example
    ///     POST /api/seed
    ///     { "mode": "Scan" }
    ///
    /// ### Dummy example
    ///     POST /api/seed
    ///     { "mode": "Dummy", "dummyHouses": "MediaHouseA,MediaHouseB", "dummyFilesPerHouse": 20 }
    /// </remarks>
    /// <param name="request">Seed options</param>
    /// <param name="ct">Cancellation token</param>
    /// <response code="200">Seed completed — returns a summary</response>
    /// <response code="400">Invalid request body</response>
    /// <response code="401">Missing API key</response>
    /// <response code="403">Invalid API key</response>
    [HttpPost]
    [ProducesResponseType(typeof(SeedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Seed([FromBody] SeedRequest request, CancellationToken ct)
    {
        if (request.DummyFilesPerHouse is < 1 or > 10_000)
            return BadRequest(new { error = "dummyFilesPerHouse must be between 1 and 10 000." });

        var result = await seedService.SeedAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Scan the sound storage bucket for <c>*_metadata.jsonld</c> files and seed the database
    /// with <see cref="SoundSeedResult"/> entities.
    /// </summary>
    /// <remarks>
    /// Walks every <c>*_metadata.jsonld</c> file found in the configured sound bucket/container,
    /// parses the Croissant JSON-LD, and upserts <c>SoundDataset</c> + <c>SoundFileRecord</c> rows.
    /// Already-present records (matched by <c>ContentUrl</c>) are skipped — the operation is idempotent.
    ///
    /// ### Example
    ///     POST /api/seed/sound
    /// </remarks>
    /// <param name="ct">Cancellation token</param>
    /// <response code="200">Sound seed completed — returns a summary</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Insufficient permissions</response>
    [HttpPost("sound")]
    [ProducesResponseType(typeof(SoundSeedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(AuthErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SeedSound(CancellationToken ct)
    {
        var result = await soundSeedService.SeedAsync(ct);
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
            FilesDeleted = await DeleteTableRowsAsync(["FileRecords", "filerecords", "file_records"], ct),
            DatasetsDeleted = await DeleteTableRowsAsync(["Datasets", "datasets"], ct),

            SoundFilesDeleted = await DeleteTableRowsAsync(["SoundFileRecords", "soundfilerecords", "sound_file_records"], ct),
            SoundDatasetsDeleted = await DeleteTableRowsAsync(["SoundDatasets", "sounddatasets", "sound_datasets"], ct)
        };

        await tx.CommitAsync(ct);

        result.Message =
            $"Reset complete. Deleted {result.DatasetsDeleted} datasets, {result.FilesDeleted} files, " +
            $"{result.KeywordsDeleted} keywords, {result.ThemesDeleted} themes, " +
            $"{result.SoundDatasetsDeleted} sound datasets, and {result.SoundFilesDeleted} sound files.";

        logger.LogWarning(
            "Database reset performed by {User}. Deleted datasets={DatasetsDeleted}, files={FilesDeleted}, soundDatasets={SoundDatasetsDeleted}, soundFiles={SoundFilesDeleted}.",
            User?.Identity?.Name ?? "unknown",
            result.DatasetsDeleted,
            result.FilesDeleted,
            result.SoundDatasetsDeleted,
            result.SoundFilesDeleted);

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
