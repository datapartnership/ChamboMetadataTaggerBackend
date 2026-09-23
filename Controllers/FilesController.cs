using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.DTOs;
using MetadataTagger.Options;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Serves text file content from the configured storage backend (local disk or S3-compatible store).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Read")]
public class FilesController(
    ApplicationDbContext db,
    IFileStorageService storage,
    TextChunkService chunks,
    IOptions<RateLimitOptions> rateLimitOptions) : ControllerBase
{
    /// <summary>Download a file's content as shuffled paragraphs.</summary>
    /// <param name="id">The unique file record identifier, e.g. <c>file-2024-001005c4</c></param>
    /// <response code="200">The file content with paragraphs in randomised order</response>
    /// <response code="404">File record not found or file missing on storage</response>
    /// <response code="429">Daily download limit reached</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Download(string id)
    {
        var record = await db.FileRecords.FirstOrDefaultAsync(f => f.Id == id);
        if (record is null)
            return NotFound(new { error = $"No file record with id '{id}'." });

        var key = record.ContentUrl;
        if (string.IsNullOrEmpty(key))
            return NotFound(new { error = $"No content path stored for record '{id}'." });

        // ── Daily rate limit check ────────────────────────────────────────────
        var limit = rateLimitOptions.Value.DailyFileDownloadLimit;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? "anonymous";

        if (limit > 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var log = await db.FileDownloadLogs
                .FirstOrDefaultAsync(l => l.UserId == userId && l.Date == today);

            var used = log?.Count ?? 0;
            if (used >= limit)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "Daily download limit reached.", limit, used });
        }

        // ── Serve content ─────────────────────────────────────────────────────
        try
        {
            var stream = await storage.OpenReadAsync(key);
            var paragraphs = chunks.ExtractParagraphs(stream);

            var shuffled = paragraphs.ToArray();
            Random.Shared.Shuffle(shuffled);
            var content = string.Join("\n\n", shuffled);

            // ── Increment download count ──────────────────────────────────────
            if (limit > 0)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var log = await db.FileDownloadLogs
                    .FirstOrDefaultAsync(l => l.UserId == userId && l.Date == today);

                if (log is null)
                    db.FileDownloadLogs.Add(new FileDownloadLog { UserId = userId, Date = today, Count = 1 });
                else
                    log.Count++;

                await db.SaveChangesAsync();
            }

            return Content(content, "text/plain");
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"File not found in storage for record '{id}'." });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new { error = $"Invalid file path for record '{id}'." });
        }
    }
}
