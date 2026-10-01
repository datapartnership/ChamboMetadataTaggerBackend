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
    private readonly IFileStorageService _storage;
    private readonly ISupervisorService _supervisorService;
    private readonly ILogger<AdminController> _logger;
    private readonly ApplicationDbContext _dbContext;

    public AdminController(
        IUserService userService,
        IFileService fileService,
        IFileStorageService storage,
        ISupervisorService supervisorService,
        ILogger<AdminController> logger,
        ApplicationDbContext dbContext)
    {
        _userService = userService;
        _fileService = fileService;
        _storage = storage;
        _supervisorService = supervisorService;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Imports files, tags and tagging assignments from a plain-SQL pg_dump of the old
    /// database, attributing all taggings to <paramref name="taggerEmail"/> and all
    /// assignments/approvals to <paramref name="approverEmail"/>. Replaces existing files,
    /// assignments and tags.
    /// </summary>
    /// <remarks>
    ///     curl -X POST https://host/api/admin/import-taggings -H "Authorization: Bearer $TOKEN" \
    ///       -F dump=@old-prod.sql -F taggerEmail=tagger@example.com -F approverEmail=admin@example.com
    /// </remarks>
    [HttpPost("import-taggings")]
    [RequestSizeLimit(1024L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1024L * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<TaggingImportResult>>> ImportTaggings(
        IFormFile dump,
        [FromForm] string taggerEmail,
        [FromForm] string approverEmail,
        [FromServices] TaggingImportService importService,
        CancellationToken ct)
    {
        if (dump == null || dump.Length == 0)
            return BadRequest(ApiResponse<TaggingImportResult>.ErrorResponse("A dump file is required."));
        if (string.IsNullOrWhiteSpace(taggerEmail) || string.IsNullOrWhiteSpace(approverEmail))
            return BadRequest(ApiResponse<TaggingImportResult>.ErrorResponse("taggerEmail and approverEmail are required."));

        try
        {
            await using var stream = dump.OpenReadStream();
            var result = await importService.ImportAsync(stream, taggerEmail, approverEmail, ct);
            return Ok(ApiResponse<TaggingImportResult>.SuccessResponse(result));
        }
        catch (FormatException ex)
        {
            return BadRequest(ApiResponse<TaggingImportResult>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while importing taggings from {FileName}", dump.FileName);
            return StatusCode(500, ApiResponse<TaggingImportResult>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<PagedResponse<UserDto>>>> GetAllUsers([FromQuery] PaginationParams pagination)
    {
        try
        {
            var users = await _userService.GetAllUsersAsync(pagination);
            return Ok(ApiResponse<PagedResponse<UserDto>>.SuccessResponse(users));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting all users");
            return StatusCode(500, ApiResponse<PagedResponse<UserDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
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

    [HttpPost("users/bulk")]
    public async Task<ActionResult<ApiResponse<BulkCreateUsersResponse>>> BulkCreateUsers([FromBody] BulkCreateUsersRequest request)
    {
        if (request.Users == null || request.Users.Count == 0)
        {
            return BadRequest(ApiResponse<BulkCreateUsersResponse>.ErrorResponse("No users provided"));
        }

        var validRoles = new[] { UserRoles.Admin, UserRoles.Tagger, UserRoles.Supervisor };
        var invalidRoleEntry = request.Users.FirstOrDefault(u => !validRoles.Contains(u.Role));
        if (invalidRoleEntry != null)
        {
            return BadRequest(ApiResponse<BulkCreateUsersResponse>.ErrorResponse(
                $"Invalid role '{invalidRoleEntry.Role}' for user '{invalidRoleEntry.Username}'. Must be 'Admin', 'Tagger', or 'Supervisor'"));
        }

        try
        {
            var result = await _userService.BulkCreateUsersAsync(request.Users);
            return Ok(ApiResponse<BulkCreateUsersResponse>.SuccessResponse(result,
                $"{result.SucceededCount} user(s) created, {result.FailedCount} failed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during bulk user creation");
            return StatusCode(500, ApiResponse<BulkCreateUsersResponse>.ErrorResponse($"An error occurred: {ex.Message}"));
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
    public async Task<ActionResult<ApiResponse<PagedResponse<UserDto>>>> GetTaggers([FromQuery] PaginationParams pagination)
    {
        try
        {
            var taggers = await _userService.GetTaggersAsync(pagination);
            return Ok(ApiResponse<PagedResponse<UserDto>>.SuccessResponse(taggers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting taggers");
            return StatusCode(500, ApiResponse<PagedResponse<UserDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lists the immediate children of <paramref name="folder"/> (sub-folders first, then files),
    /// paged over those children.
    /// </summary>
    [HttpGet("blobs")]
    public async Task<ActionResult<ApiResponse<FolderPagedResponse<BlobFileDto>>>> GetAllBlobs(
        [FromQuery] string? folder = null,
        [FromQuery] PaginationParams? pagination = null)
    {
        pagination ??= new PaginationParams();
        try
        {
            var dir = FolderListing.NormalizeFolder(folder);
            var allBlobs = (await _storage.ListFilesAsync(dir))
                .Where(e => CatalogKey.IsCollectionContent(e.Key))
                .Select(FileService.ToBlobFile)
                .ToList();

            var (folders, files) = FolderListing.Split(allBlobs, b => b.BlobName, dir);

            IEnumerable<BlobFileDto> sorted = pagination.SortBy?.ToLowerInvariant() switch
            {
                "blobname" => pagination.IsDescending
                    ? files.OrderByDescending(b => b.BlobName)
                    : files.OrderBy(b => b.BlobName),
                "filesize" => pagination.IsDescending
                    ? files.OrderByDescending(b => b.FileSize)
                    : files.OrderBy(b => b.FileSize),
                "lastmodified" => pagination.IsDescending
                    ? files.OrderByDescending(b => b.LastModified)
                    : files.OrderBy(b => b.LastModified),
                "contenttype" => pagination.IsDescending
                    ? files.OrderByDescending(b => b.ContentType)
                    : files.OrderBy(b => b.ContentType),
                "filecategory" => pagination.IsDescending
                    ? files.OrderByDescending(b => b.FileCategory)
                    : files.OrderBy(b => b.FileCategory),
                _ => files.OrderBy(b => b.BlobName)
            };

            var (pageFolders, pageFiles) = FolderListing.Page(folders, sorted.ToList(), pagination.Page, pagination.PageSize);
            var items = pageFolders
                .Select(f => new BlobFileDto
                {
                    BlobName = f.Path,
                    FileUrl = "",
                    IsDirectory = true,
                    FileCount = f.FileCount
                })
                .Concat(pageFiles)
                .ToList();

            var pagedResult = new FolderPagedResponse<BlobFileDto>
            {
                Items = items,
                Page = pagination.Page,
                PageSize = pagination.PageSize,
                TotalCount = folders.Count + files.Count,
                Folder = dir,
                TotalFileCount = allBlobs.Count
            };

            return Ok(ApiResponse<FolderPagedResponse<BlobFileDto>>.SuccessResponse(pagedResult));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting all blobs");
            return StatusCode(500, ApiResponse<FolderPagedResponse<BlobFileDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("blobs/preview")]
    public async Task<ActionResult<ApiResponse<FilePreviewDto>>> GetBlobPreview([FromQuery] string blobName, [FromQuery] int expiryMinutes = 60)
    {
        try
        {
            var entry = await _storage.GetEntryAsync(blobName);
            if (entry == null)
            {
                return NotFound(ApiResponse<FilePreviewDto>.ErrorResponse($"Blob '{blobName}' not found"));
            }

            var previewUrl = await _storage.GetReadUrlAsync(blobName, TimeSpan.FromMinutes(expiryMinutes));
            if (previewUrl == null)
            {
                return BadRequest(ApiResponse<FilePreviewDto>.ErrorResponse(FileService.PreviewUnavailableMessage));
            }

            var contentType = FileCategoryHelper.GetContentType(blobName);
            var preview = new FilePreviewDto
            {
                FileId = 0,
                FileName = blobName,
                BlobName = blobName,
                PreviewUrl = previewUrl,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
                FileSize = entry.Size,
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
            var response = await _fileService.SyncFilesFromBlobStorageAsync();

            return Ok(ApiResponse<SyncBlobFilesResponse>.SuccessResponse(response,
                $"Synced {response.ImportedFiles} new files from blob storage"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while syncing blob files");
            return StatusCode(500, ApiResponse<SyncBlobFilesResponse>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("files")]
    public async Task<ActionResult<ApiResponse<PagedResponse<FileMetadataDto>>>> GetAssignedFiles([FromQuery] PaginationParams pagination)
    {
        try
        {
            var files = await _fileService.GetAssignedFilesAsync(pagination);
            return Ok(ApiResponse<PagedResponse<FileMetadataDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting assigned files");
            return StatusCode(500, ApiResponse<PagedResponse<FileMetadataDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lists the unassigned files' immediate children of <paramref name="folder"/> (sub-folders first,
    /// then files), paged over those children.
    /// </summary>
    [HttpGet("files/unassigned")]
    public async Task<ActionResult<ApiResponse<FolderPagedResponse<FileMetadataDto>>>> GetUnassignedFiles(
        [FromQuery] PaginationParams pagination,
        [FromQuery] string? folder = null)
    {
        try
        {
            var files = await _fileService.GetUnassignedFilesAsync(pagination, folder);
            return Ok(ApiResponse<FolderPagedResponse<FileMetadataDto>>.SuccessResponse(files));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting unassigned files");
            return StatusCode(500, ApiResponse<FolderPagedResponse<FileMetadataDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
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

            return await this.StreamStorageFileAsync(_storage, file.BlobName, HttpContext.RequestAborted);
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
    public async Task<ActionResult<ApiResponse<PagedResponse<TaggingProgressDto>>>> GetTaggingProgress([FromQuery] PaginationParams pagination)
    {
        try
        {
            var progress = await _fileService.GetTaggingProgressAsync(pagination);
            return Ok(ApiResponse<PagedResponse<TaggingProgressDto>>.SuccessResponse(progress));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting tagging progress");
            return StatusCode(500, ApiResponse<PagedResponse<TaggingProgressDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
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
    public async Task<ActionResult<ApiResponse<PagedResponse<StudentSupervisorDto>>>> GetSupervisorAssignments([FromQuery] PaginationParams pagination)
    {
        try
        {
            var assignments = await _supervisorService.GetAllSupervisorAssignmentsAsync(pagination);
            return Ok(ApiResponse<PagedResponse<StudentSupervisorDto>>.SuccessResponse(assignments));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting supervisor assignments");
            return StatusCode(500, ApiResponse<PagedResponse<StudentSupervisorDto>>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("clear-assignments")]
    public async Task<ActionResult<ApiResponse<bool>>> ClearAssignments([FromServices] CatalogTagPublisher publisher)
    {
        try
        {
            _logger.LogWarning("Clear assignments initiated by admin");

            var taggedKeys = await TaggedBlobNamesAsync();
            await _dbContext.FileTags.ExecuteDeleteAsync();
            await _dbContext.FileAssignments.ExecuteDeleteAsync();
            await _dbContext.StudentSupervisors.ExecuteDeleteAsync();
            await publisher.ApplyAsync(taggedKeys);

            _logger.LogWarning("Clear assignments completed successfully");
            return Ok(ApiResponse<bool>.SuccessResponse(true, "All file assignments, taggings, and tagger-supervisor assignments have been cleared."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while clearing assignments");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpPost("reset-database")]
    public async Task<ActionResult<ApiResponse<bool>>> ResetDatabase()
    {
        try
        {
            _logger.LogWarning("Database reset initiated by admin");

            var usersTable = _dbContext.Model.FindEntityType(typeof(User))!.GetTableName();
            var tables = _dbContext.Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .Where(t => t != null && t != usersTable)
                .Distinct()
                .Select(t => $"\"{t}\"");
            var truncateSql = "TRUNCATE TABLE " + string.Join(", ", tables) + " RESTART IDENTITY";

            // Admin accounts are kept so the caller can still sign in afterwards.
            await using var tx = await _dbContext.Database.BeginTransactionAsync();
            await _dbContext.Database.ExecuteSqlRawAsync(truncateSql);
            await _dbContext.Users
                .Where(u => u.Role != UserRoles.Admin)
                .ExecuteDeleteAsync();
            await tx.CommitAsync();

            _logger.LogWarning("Database reset completed successfully");
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Database reset successfully. All tables have been emptied except admin users."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while resetting the database");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    [HttpGet("export/annotations")]
    public async Task<IActionResult> ExportAnnotationsCsv()
    {
        try
        {
            var files = await _dbContext.FileMetadata
                .Include(f => f.Tags)
                .Where(f => f.Status == FileTaggingStatus.SubmittedToSupervisor ||
                            f.Status == FileTaggingStatus.ApprovedBySupervisor)
                .OrderBy(f => f.Id)
                .AsNoTracking()
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("FileId,FilePath,Status,Keywords,Themes");

            foreach (var file in files)
            {
                var keywords = file.Tags
                    .Where(t => t.TagKey.Equals("Keywords", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.TagValue);

                var themes = file.Tags
                    .Where(t => t.TagKey.Equals("Theme", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.TagValue);

                sb.AppendLine(string.Join(",", new[]
                {
                    file.Id.ToString(),
                    CsvEscape(file.BlobName ?? file.FileName),
                    file.Status.ToString(),
                    CsvEscape(string.Join("|", keywords)),
                    CsvEscape(string.Join("|", themes))
                }));
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"annotations_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while exporting annotations CSV");
            return StatusCode(500, ApiResponse<object>.ErrorResponse($"An error occurred: {ex.Message}"));
        }
    }

    private Task<List<string>> TaggedBlobNamesAsync() =>
        _dbContext.FileMetadata
            .Where(f => f.BlobName != null && f.Tags.Any())
            .Select(f => f.BlobName!)
            .ToListAsync();

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
