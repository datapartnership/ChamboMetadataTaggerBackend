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
/// Serves collection file content from catalog storage (local disk, S3-compatible store, or Azure Blob Storage).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Read")]
public class FilesController(
    ApplicationDbContext db,
    IFileStorageService storage,
    TextChunkService chunks,
    CollectionBundleService bundles,
    IOptions<RateLimitOptions> rateLimitOptions) : ControllerBase
{
    /// <summary>Download a file.</summary>
    /// <remarks>
    /// Text files (TXT, MD, and PDF) are returned as plain text with their paragraphs in
    /// randomised order and count towards the daily download limit. Audio, video, and
    /// transcription files are streamed as-is.
    /// </remarks>
    /// <param name="id">The unique file identifier, e.g. <c>file-1a2b3c4d5e6f7a8b</c></param>
    /// <response code="200">The file content</response>
    /// <response code="404">File record not found or file missing on storage</response>
    /// <response code="422">The PDF could not be read</response>
    /// <response code="429">Daily text download limit reached</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Download(string id)
    {
        var record = await db.CollectionFiles.FirstOrDefaultAsync(f => f.Id == id);
        if (record is null)
            return NotFound(new ErrorResponse { error = $"No file record with id '{id}'." });

        try
        {
            if (record.Kind != FileKind.Text)
            {
                var stream = await storage.OpenReadAsync(record.ContentUrl);
                return File(stream, record.EncodingFormat, record.Name, enableRangeProcessing: stream.CanSeek);
            }

            return await DownloadTextAsync(record);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse { error = $"File not found in storage for record '{id}'." });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new ErrorResponse { error = $"Invalid file path for record '{id}'." });
        }
        catch (InvalidDataException)
        {
            return UnprocessableEntity(new ErrorResponse { error = $"The PDF for record '{id}' could not be read (corrupt or encrypted)." });
        }
    }

    /// <summary>Download an audio/video file together with its transcription as a ZIP.</summary>
    /// <remarks>
    /// The ZIP contains the media file and its paired transcription, when one exists.
    /// Passing a transcription's Id bundles it with the media file it belongs to.
    /// Text files cannot be bundled.
    ///
    /// ### Example
    ///     GET /api/files/file-1a2b3c4d5e6f7a8b/bundle
    /// </remarks>
    /// <param name="id">The unique file identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">ZIP archive with the media file and its transcription</response>
    /// <response code="404">No audio/video/transcription record, or a member is missing on storage</response>
    [HttpGet("{id}/bundle")]
    [Produces("application/zip")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadBundle(string id, CancellationToken ct)
    {
        try
        {
            var bundle = await bundles.GetFileBundleAsync(id, ct);
            if (bundle is null)
                return NotFound(new ErrorResponse { error = $"No audio, video, or transcription file with id '{id}'." });

            return new ZipStreamResult(bundle);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse { error = $"Media or transcription file not found in storage for record '{id}'." });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new ErrorResponse { error = $"Invalid file path for record '{id}'." });
        }
    }

    private async Task<IActionResult> DownloadTextAsync(CollectionFile record)
    {
        // ── Daily rate limit check ────────────────────────────────────────────
        var limit = rateLimitOptions.Value.DailyFileDownloadLimit;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? "anonymous";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (limit > 0)
        {
            var log = await db.FileDownloadLogs
                .FirstOrDefaultAsync(l => l.UserId == userId && l.Date == today);

            var used = log?.Count ?? 0;
            if (used >= limit)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "Daily download limit reached.", limit, used });
        }

        // ── Serve content ─────────────────────────────────────────────────────
        await using var stream = await storage.OpenReadAsync(record.ContentUrl, HttpContext.RequestAborted);
        var paragraphs = record.EncodingFormat == "application/pdf"
            ? await chunks.ExtractPdfParagraphsAsync(stream, HttpContext.RequestAborted)
            : chunks.ExtractParagraphs(stream);
        var shuffled = paragraphs.ToArray();
        Random.Shared.Shuffle(shuffled);
        var content = string.Join("\n\n", shuffled);

        // ── Increment download count ──────────────────────────────────────────
        if (limit > 0)
        {
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
}
