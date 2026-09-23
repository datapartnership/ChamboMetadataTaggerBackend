using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Services;

namespace MetadataTagging.Controllers;

/// <summary>
/// Streams audio files (FLAC, MP3, etc.) from the configured sound storage backend.
/// </summary>
[ApiController]
[Route("api/sound/files")]
[Authorize(Policy = "Read")]
public class SoundFilesController(
    ApplicationDbContext db,
    [FromKeyedServices("sound")] IFileStorageService soundStorage) : ControllerBase
{
    /// <summary>Download an audio file by its sound file record ID.</summary>
    /// <param name="id">The unique sound file record identifier.</param>
    /// <response code="200">The audio file stream</response>
    /// <response code="404">Sound file record not found or file missing on storage</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse),    StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string id)
    {
        var record = await db.SoundFileRecords.FirstOrDefaultAsync(f => f.Id == id);
        if (record is null)
            return NotFound(new { error = $"No sound file record with id '{id}'." });

        var key = record.ContentUrl;
        if (string.IsNullOrEmpty(key))
            return NotFound(new { error = $"No content path stored for sound record '{id}'." });

        try
        {
            var stream = await soundStorage.OpenReadAsync(key);
            return File(stream, record.EncodingFormat ?? "audio/flac", record.Name);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"Audio file not found in storage for record '{id}'." });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new { error = $"Invalid file path for sound record '{id}'." });
        }
    }
}

