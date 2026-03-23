using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagging.Services;
using System.Security.Claims;

namespace MetadataTagging.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFileService _fileService;
    private readonly IStorageService _blobService;
    private readonly ISupervisorService _supervisorService;
    private readonly ILogger<AdminController> _logger;
    private readonly ApplicationDbContext _dbContext;

    public AdminController(
        IUserService userService,
        IFileService fileService,
        IStorageService blobService,
        ISupervisorService supervisorService,
        ILogger<AdminController> logger,
        ApplicationDbContext dbContext)
    {
        _userService = userService;
        _fileService = fileService;
        _blobService = blobService;
        _supervisorService = supervisorService;
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAllUsers()
    {
        try
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(ApiResponse<IEnumerable<UserDto>>.SuccessResponse(users));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting all users");
            return StatusCode(500, ApiResponse<IEnumerable<UserDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("users/{userId}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(int userId)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(userId);

            if (user == null)
            {
                return NotFound(ApiResponse<UserDto>.ErrorResponse("User not found"));
            }

            return Ok(ApiResponse<UserDto>.SuccessResponse(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting user with ID: {UserId}", userId);
            return StatusCode(500, ApiResponse<UserDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("users")]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            if (request.Role != UserRoles.Admin && request.Role != UserRoles.Tagger && request.Role != UserRoles.Supervisor)
            {
                return BadRequest(ApiResponse<UserDto>.ErrorResponse("Invalid role. Must be 'Admin', 'Tagger', or 'Supervisor'"));
            }

            var user = await _userService.CreateUserAsync(request);
            return CreatedAtAction(nameof(GetUser), new { userId = user.Id },
                ApiResponse<UserDto>.SuccessResponse(user, "User created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while creating user with email: {Email}", request.Email);
            return BadRequest(ApiResponse<UserDto>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while creating user with email: {Email}", request.Email);
            return StatusCode(500, ApiResponse<UserDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPut("users/{userId}")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUser(int userId, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var result = await _userService.UpdateUserAsync(userId, request);

            if (!result)
            {
                return NotFound(ApiResponse<bool>.ErrorResponse("User not found"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "User updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while updating user with ID: {UserId}", userId);
            return BadRequest(ApiResponse<bool>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating user with ID: {UserId}", userId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpDelete("users/{userId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteUser(int userId)
    {
        try
        {
            var result = await _userService.DeleteUserAsync(userId);

            if (!result)
            {
                return NotFound(ApiResponse<bool>.ErrorResponse("User not found"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "User deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while deleting user with ID: {UserId}", userId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("taggers")]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetTaggers()
    {
        try
        {
            var taggers = await _userService.GetTaggersAsync();
            return Ok(ApiResponse<IEnumerable<UserDto>>.SuccessResponse(taggers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting taggers");
            return StatusCode(500, ApiResponse<IEnumerable<UserDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("blobs")]
    public async Task<ActionResult<ApiResponse<IEnumerable<BlobFileDto>>>> GetAllBlobs([FromQuery] string? folder = null)
    {
        try
        {
            var blobs = await _blobService.ListBlobsAsync(prefix: folder);
            return Ok(ApiResponse<IEnumerable<BlobFileDto>>.SuccessResponse(blobs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting all blobs");
            return StatusCode(500, ApiResponse<IEnumerable<BlobFileDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("blobs/preview")]
    public async Task<ActionResult<ApiResponse<FilePreviewDto>>> GetBlobPreview([FromQuery] string blobName, [FromQuery] int expiryMinutes = 60)
    {
        try
        {
            if (!await _blobService.BlobExistsAsync(blobName))
            {
                return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse($"Blob '{blobName}' not found"));
            }

            var previewUrl = await _blobService.GetBlobSasUrlAsync(blobName, expiryMinutes);
            var blobUrl = await _blobService.GetBlobUrlAsync(blobName);

            var blobs = await _blobService.ListBlobsAsync();
            var blobInfo = blobs.FirstOrDefault(b => b.BlobName == blobName);

            var contentType = blobInfo?.ContentType;
            var preview = new FilePreviewDto
            {
                FileId = 0,
                FileName = blobName,
                BlobName = blobName,
                PreviewUrl = previewUrl,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
                FileSize = blobInfo?.FileSize ?? 0,
                ContentType = contentType,
                FileCategory = FileCategoryHelper.FromContentType(contentType, blobName).ToString()
            };

            return Ok(ApiResponse<FilePreviewDto>.SuccessResponse(preview));
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Blob not found: {BlobName}", blobName);
            return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting blob preview for: {BlobName}", blobName);
            return StatusCode(500, ApiResponse<FilePreviewDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("sync-blobs")]
    public async Task<ActionResult<ApiResponse<SyncBlobFilesResponse>>> SyncBlobFiles()
    {
        try
        {
            var blobs = await _blobService.ListBlobsAsync();
            var totalBlobs = blobs.Count();
            var importedCount = await _fileService.SyncFilesFromBlobStorageAsync();

            var response = new SyncBlobFilesResponse
            {
                TotalBlobs = totalBlobs,
                ImportedFiles = importedCount,
                ExistingFiles = totalBlobs - importedCount
            };

            return Ok(ApiResponse<SyncBlobFilesResponse>.SuccessResponse(response,
                $"Synced {importedCount} new files from blob storage"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while syncing blob files");
            return StatusCode(500, ApiResponse<SyncBlobFilesResponse>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files")]
    public async Task<ActionResult<ApiResponse<IEnumerable<FileMetadataDto>>>> GetAllFiles()
    {
        try
        {
            var files = await _fileService.GetAllFilesAsync();
            return Ok(ApiResponse<IEnumerable<FileMetadataDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting all files");
            return StatusCode(500, ApiResponse<IEnumerable<FileMetadataDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/unassigned")]
    public async Task<ActionResult<ApiResponse<IEnumerable<FileMetadataDto>>>> GetUnassignedFiles()
    {
        try
        {
            var files = await _fileService.GetUnassignedFilesAsync();
            return Ok(ApiResponse<IEnumerable<FileMetadataDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting unassigned files");
            return StatusCode(500, ApiResponse<IEnumerable<FileMetadataDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("files")]
    public async Task<ActionResult<ApiResponse<FileMetadataDto>>> CreateFileMetadata([FromBody] CreateFileMetadataRequest request)
    {
        try
        {
            var file = await _fileService.CreateFileMetadataAsync(request);
            return CreatedAtAction(nameof(GetFile), new { fileId = file.Id },
                ApiResponse<FileMetadataDto>.SuccessResponse(file, "File metadata created successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while creating file metadata for: {BlobName}", request.BlobName);
            return StatusCode(500, ApiResponse<FileMetadataDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}")]
    public async Task<ActionResult<ApiResponse<FileMetadataDto>>> GetFile(int fileId)
    {
        try
        {
            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound(ApiResponse<FileMetadataDto>.ErrorResponse("File not found"));
            }

            return Ok(ApiResponse<FileMetadataDto>.SuccessResponse(file));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting file with ID: {FileId}", fileId);
            return StatusCode(500, ApiResponse<FileMetadataDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}/preview")]
    public async Task<ActionResult<ApiResponse<FilePreviewDto>>> GetFilePreview(int fileId, [FromQuery] int expiryMinutes = 60)
    {
        try
        {
            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse("File not found"));
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
            _logger.LogWarning(ex, "File not found for preview: {FileId}", fileId);
            return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting file preview for ID: {FileId}", fileId);
            return StatusCode(500, ApiResponse<FilePreviewDto>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPut("files/{fileId}/audio-metadata")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateAudioMetadata(int fileId, [FromBody] UpdateAudioMetadataRequest request)
    {
        try
        {
            var result = await _fileService.UpdateAudioMetadataAsync(fileId, request.DurationSeconds);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to update audio metadata. File may not exist or may not be an audio file."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Audio metadata updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating audio metadata for file {FileId}", fileId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files/{fileId}/stream")]
    public async Task<IActionResult> StreamFile(int fileId)
    {
        try
        {
            var file = await _fileService.GetFileByIdAsync(fileId);

            if (file == null)
            {
                return NotFound("File not found");
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
            _logger.LogError(ex, "Error streaming file {FileId}", fileId);
            return StatusCode(500, "An error occurred while streaming the file");
        }
    }

    [HttpPost("assign-file")]
    public async Task<ActionResult<ApiResponse<bool>>> AssignFileToUser([FromBody] AssignFileRequest request)
    {
        try
        {
            var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (adminIdClaim == null || !int.TryParse(adminIdClaim, out int adminId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid admin credentials"));
            }

            var result = await _fileService.AssignFileToUserAsync(request.FileId, request.UserId, adminId);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to assign file. Check if file and user exist, and user is a Tagger."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "File assigned successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while assigning file {FileId} to user {UserId}", request.FileId, request.UserId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("assign-blob-file")]
    public async Task<ActionResult<ApiResponse<bool>>> AssignBlobFileToUser([FromBody] AssignBlobFileRequest request)
    {
        try
        {
            var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (adminIdClaim == null || !int.TryParse(adminIdClaim, out int adminId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid admin credentials"));
            }

            var result = await _fileService.AssignBlobFileToUserAsync(request.BlobName, request.UserId, adminId);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to assign blob file. Check if blob exists and user is a Tagger."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Blob file imported and assigned successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while assigning blob file {BlobName} to user {UserId}", request.BlobName, request.UserId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("assign-multiple-files")]
    public async Task<ActionResult<ApiResponse<AssignMultipleFilesResult>>> AssignMultipleFilesToUser([FromBody] AssignMultipleFilesRequest request)
    {
        try
        {
            var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (adminIdClaim == null || !int.TryParse(adminIdClaim, out int adminId))
            {
                return Unauthorized(ApiResponse<AssignMultipleFilesResult>.ErrorResponse("Invalid admin credentials"));
            }

            if (request.BlobNames == null || request.BlobNames.Count == 0)
            {
                return BadRequest(ApiResponse<AssignMultipleFilesResult>.ErrorResponse("Blob names list cannot be empty"));
            }

            var result = await _fileService.AssignMultipleFilesToUserAsync(request.BlobNames, request.UserId, adminId);

            if (result.SuccessfullyAssigned == 0)
            {
                return BadRequest(ApiResponse<AssignMultipleFilesResult>.ErrorResponse(
                    "Failed to assign any files. Check if blobs exist and user is a Tagger."));
            }

            var message = result.Failed > 0
                ? $"Assigned {result.SuccessfullyAssigned} of {result.TotalRequested} files successfully. {result.Failed} failed."
                : $"All {result.SuccessfullyAssigned} files assigned successfully";

            return Ok(ApiResponse<AssignMultipleFilesResult>.SuccessResponse(result, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while assigning multiple files to user {UserId}", request.UserId);
            return StatusCode(500, ApiResponse<AssignMultipleFilesResult>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("tagging-progress")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TaggingProgressDto>>>> GetTaggingProgress()
    {
        try
        {
            var progress = await _fileService.GetTaggingProgressAsync();
            return Ok(ApiResponse<IEnumerable<TaggingProgressDto>>.SuccessResponse(progress));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting tagging progress");
            return StatusCode(500, ApiResponse<IEnumerable<TaggingProgressDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("assign-student-to-supervisor")]
    public async Task<ActionResult<ApiResponse<bool>>> AssignStudentToSupervisor([FromBody] AssignStudentToSupervisorRequest request)
    {
        try
        {
            var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (adminIdClaim == null || !int.TryParse(adminIdClaim, out int adminId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse("Invalid admin credentials"));
            }

            var result = await _supervisorService.AssignStudentToSupervisorAsync(request.StudentId, request.SupervisorId, adminId);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to assign student. Check if student is a Tagger and supervisor exists with Supervisor role."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Student assigned to supervisor successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while assigning student {StudentId} to supervisor {SupervisorId}", request.StudentId, request.SupervisorId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("unassign-student-from-supervisor")]
    public async Task<ActionResult<ApiResponse<bool>>> UnassignStudentFromSupervisor([FromBody] AssignStudentToSupervisorRequest request)
    {
        try
        {
            var result = await _supervisorService.UnassignStudentFromSupervisorAsync(request.StudentId, request.SupervisorId);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to unassign student. Assignment may not exist."));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Student unassigned from supervisor successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while unassigning student {StudentId} from supervisor {SupervisorId}", request.StudentId, request.SupervisorId);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("supervisor-assignments")]
    public async Task<ActionResult<ApiResponse<IEnumerable<StudentSupervisorDto>>>> GetSupervisorAssignments()
    {
        try
        {
            var assignments = await _supervisorService.GetAllSupervisorAssignmentsAsync();
            return Ok(ApiResponse<IEnumerable<StudentSupervisorDto>>.SuccessResponse(assignments));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting supervisor assignments");
            return StatusCode(500, ApiResponse<IEnumerable<StudentSupervisorDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("reset-database")]
    public async Task<ActionResult<ApiResponse<bool>>> ResetDatabase()
    {
        try
        {
            _logger.LogWarning("Database reset initiated by admin");

            await _dbContext.FileTags.ExecuteDeleteAsync();
            await _dbContext.FileAssignments.ExecuteDeleteAsync();
            await _dbContext.StudentSupervisors.ExecuteDeleteAsync();
            await _dbContext.FileMetadata.ExecuteDeleteAsync();
            await _dbContext.Users
                .Where(u => u.Role != UserRoles.Admin)
                .ExecuteDeleteAsync();

            _logger.LogWarning("Database reset completed successfully");
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Database reset successfully. All non-admin data has been cleared."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while resetting the database");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
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
}
