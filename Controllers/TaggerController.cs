using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagging.Services;
using System.Security.Claims;

namespace MetadataTagging.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = UserRoles.Tagger)]
public class TaggerController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly IFileStorageService _storage;

    public TaggerController(IFileService fileService, IFileStorageService storage)
    {
        _fileService = fileService;
        _storage = storage;
    }

    [HttpGet("my-files")]
    public async Task<ActionResult<ApiResponse<PagedResponse<FileMetadataDto>>>> GetMyAssignedFiles([FromQuery] PaginationParams pagination)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(ApiResponse<PagedResponse<FileMetadataDto>>.ErrorResponse("Invalid user credentials"));
            }

            var files = await _fileService.GetFilesAssignedToUserAsync(userId, pagination);
            return Ok(ApiResponse<PagedResponse<FileMetadataDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<PagedResponse<FileMetadataDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}")]
    public async Task<ActionResult<ApiResponse<FileMetadataDto>>> GetFile(int fileId)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(ApiResponse<FileMetadataDto>.ErrorResponse("Invalid user credentials"));
            }

            var file = await _fileService.GetFileByIdAsync(fileId, userId);

            if (file == null)
            {
                return NotFound(ApiResponse<FileMetadataDto>.ErrorResponse("File not found"));
            }

            if (!file.AssignedToUserIds.Contains(userId))
            {
                return Forbid();
            }

            return Ok(ApiResponse<FileMetadataDto>.SuccessResponse(file));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<FileMetadataDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("files/{fileId}/tags")]
    public async Task<ActionResult<ApiResponse<bool>>> AddTagsToFile(int fileId, [FromBody] AddTagsRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid user credentials"));
            }

            if (request.Tags == null || request.Tags.Count == 0)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("At least one tag is required"));
            }

            var result = await _fileService.AddTagsToFileAsync(fileId, userId, request.Tags);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to add tags. File may not be assigned to you."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Tags added successfully"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("files/{fileId}/complete")]
    public async Task<ActionResult<ApiResponse<bool>>> CompleteFileTagging(int fileId)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid user credentials"));
            }

            var result = await _fileService.CompleteFileTaggingAsync(fileId, userId);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to complete file tagging. File may not be assigned to you."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "File tagging completed successfully"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}/preview")]
    public async Task<ActionResult<ApiResponse<FilePreviewDto>>> GetFilePreview(int fileId, [FromQuery] int expiryMinutes = 60)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(ApiResponse<FilePreviewDto>.ErrorResponse("Invalid user credentials"));
            }

            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse("File not found"));
            }

            if (!file.AssignedToUserIds.Contains(userId))
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(file.BlobName))
            {
                return BadRequest(ApiResponse<FilePreviewDto>.ErrorResponse("File has no associated blob"));
            }

            var previewUrl = await _storage.GetReadUrlAsync(file.BlobName, TimeSpan.FromMinutes(expiryMinutes));
            if (previewUrl == null)
            {
                return BadRequest(ApiResponse<FilePreviewDto>.ErrorResponse(FileService.PreviewUnavailableMessage));
            }

            var preview = new FilePreviewDto
            {
                FileId = file.Id,
                FileName = file.FileName,
                BlobName = file.BlobName,
                PreviewUrl = previewUrl,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
                FileSize = file.FileSize,
                ContentType = file.ContentType,
                FileCategory = FileCategoryHelper.FromContentType(file.ContentType, file.FileName).ToString(),
                DurationSeconds = file.DurationSeconds
            };

            return Ok(ApiResponse<FilePreviewDto>.SuccessResponse(preview));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<FilePreviewDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPut("files/{fileId}/audio-metadata")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateAudioMetadata(int fileId, [FromBody] UpdateAudioMetadataRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid user credentials"));
            }

            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound(ApiResponse<bool>.ErrorResponse("File not found"));
            }

            if (!file.AssignedToUserIds.Contains(userId))
            {
                return Forbid();
            }

            var result = await _fileService.UpdateAudioMetadataAsync(fileId, request.DurationSeconds);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to update audio metadata. File may not be an audio file."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Audio metadata updated successfully"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}/stream")]
    public async Task<IActionResult> StreamFile(int fileId)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized();
            }

            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound("File not found");
            }

            if (!file.AssignedToUserIds.Contains(userId))
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(file.BlobName))
            {
                return BadRequest("File has no associated blob");
            }

            return await this.StreamStorageFileAsync(_storage, file.BlobName, HttpContext.RequestAborted);
        }
        catch (FileNotFoundException)
        {
            return NotFound("Blob not found");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred while streaming the file: {ex.Message}");
        }
    }
}
