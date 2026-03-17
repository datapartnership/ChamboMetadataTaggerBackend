using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagging.Services;
using System.Security.Claims;

namespace MetadataTagging.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = UserRoles.Supervisor)]
public class SupervisorController : ControllerBase
{
    private readonly ISupervisorService _supervisorService;
    private readonly IStorageService _blobService;
    private readonly IFileService _fileService;

    public SupervisorController(
        ISupervisorService supervisorService,
        IStorageService blobService,
        IFileService fileService)
    {
        _supervisorService = supervisorService;
        _blobService = blobService;
        _fileService = fileService;
    }

    [HttpGet("my-students")]
    public async Task<ActionResult<ApiResponse<IEnumerable<StudentWithStatsDto>>>> GetMyStudents()
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<IEnumerable<StudentWithStatsDto>>.ErrorResponse("Invalid supervisor credentials"));
            }

            var students = await _supervisorService.GetSupervisorStudentsAsync(supervisorId);
            return Ok(ApiResponse<IEnumerable<StudentWithStatsDto>>.SuccessResponse(students));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<IEnumerable<StudentWithStatsDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("students/{studentId}/files")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SupervisorReviewDto>>>> GetStudentFiles(int studentId)
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<IEnumerable<SupervisorReviewDto>>.ErrorResponse("Invalid supervisor credentials"));
            }

            var files = await _supervisorService.GetStudentFilesForReviewAsync(supervisorId, studentId);
            return Ok(ApiResponse<IEnumerable<SupervisorReviewDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<IEnumerable<SupervisorReviewDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("all-student-files")]
    public async Task<ActionResult<ApiResponse<IEnumerable<SupervisorReviewDto>>>> GetAllStudentFiles()
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<IEnumerable<SupervisorReviewDto>>.ErrorResponse("Invalid supervisor credentials"));
            }

            var files = await _supervisorService.GetAllStudentFilesForSupervisorAsync(supervisorId);
            return Ok(ApiResponse<IEnumerable<SupervisorReviewDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<IEnumerable<SupervisorReviewDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}/preview")]
    public async Task<ActionResult<ApiResponse<FilePreviewDto>>> GetFilePreview(int fileId, [FromQuery] int expiryMinutes = 60)
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<FilePreviewDto>.ErrorResponse("Invalid supervisor credentials"));
            }

            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse("File not found"));
            }

            var studentFiles = await _supervisorService.GetAllStudentFilesForSupervisorAsync(supervisorId);
            var canAccess = studentFiles.Any(f => f.FileId == fileId);

            if (!canAccess)
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(file.BlobName))
            {
                return BadRequest(ApiResponse<FilePreviewDto>.ErrorResponse("File has no associated blob"));
            }

            var previewUrl = await _blobService.GetBlobSasUrlAsync(file.BlobName, expiryMinutes);

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

    [HttpGet("files/{fileId}/stream")]
    public async Task<IActionResult> StreamFile(int fileId)
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized();
            }

            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound("File not found");
            }

            var studentFiles = await _supervisorService.GetAllStudentFilesForSupervisorAsync(supervisorId);
            var canAccess = studentFiles.Any(f => f.FileId == fileId);

            if (!canAccess)
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(file.BlobName))
            {
                return BadRequest("File has no associated blob");
            }

            var rangeHeader = Request.Headers.Range.FirstOrDefault();

            if (!string.IsNullOrEmpty(rangeHeader))
            {
                return await HandleRangeRequest(file.BlobName, file.ContentType ?? "application/octet-stream", rangeHeader);
            }

            var (content, contentType, contentLength) = await _blobService.DownloadBlobAsync(file.BlobName);
            Response.Headers.AcceptRanges = "bytes";
            return File(content, contentType, enableRangeProcessing: false);
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

    private async Task<IActionResult> HandleRangeRequest(string blobName, string contentType, string rangeHeader)
    {
        var rangeValue = rangeHeader.Replace("bytes=", "");
        var parts = rangeValue.Split('-');

        if (!long.TryParse(parts[0], out long start))
        {
            return BadRequest("Invalid range header");
        }

        long? end = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? long.Parse(parts[1]) : null;
        long? length = end.HasValue ? end.Value - start + 1 : null;

        var (content, resolvedContentType, contentLength, totalSize) =
            await _blobService.DownloadBlobRangeAsync(blobName, start, length);

        var actualEnd = start + contentLength - 1;

        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ContentRange = $"bytes {start}-{actualEnd}/{totalSize}";
        Response.ContentLength = contentLength;

        return StatusCode(206, File(content, resolvedContentType, enableRangeProcessing: false));
    }

    [HttpPost("mark-file-checked")]
    public async Task<ActionResult<ApiResponse<bool>>> MarkFileAsChecked([FromBody] MarkFileCheckedRequest request)
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid supervisor credentials"));
            }

            var result = await _supervisorService.MarkFileAsCheckedAsync(
                request.FileId,
                request.StudentId,
                supervisorId,
                request.Notes);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to mark file as checked. Student may not be assigned to you or file not found."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "File marked as checked successfully"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("send-back-to-tagger")]
    public async Task<ActionResult<ApiResponse<bool>>> SendBackToTagger([FromBody] SendBackToTaggerRequest request)
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid supervisor credentials"));
            }

            var result = await _supervisorService.SendBackToTaggerAsync(
                request.FileId,
                request.StudentId,
                supervisorId,
                request.Notes);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to send file back. Student may not be assigned to you or file not found."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "File sent back to tagger for revision"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPut("files/{fileId}/tags")]
    public async Task<ActionResult<ApiResponse<bool>>> EditFileTags(int fileId, [FromBody] EditFileTagsRequest request)
    {
        try
        {
            var supervisorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (supervisorIdClaim == null || !int.TryParse(supervisorIdClaim, out int supervisorId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid supervisor credentials"));
            }

            if (request.Tags == null || request.Tags.Count == 0)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("At least one tag is required"));
            }

            var result = await _supervisorService.EditFileTagsAsync(
                fileId,
                request.StudentId,
                supervisorId,
                request.Tags,
                request.Notes);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to edit tags. Student may not be assigned to you or file not found."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "File tags updated successfully"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }
}
