using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public class FileService : IFileService
{
    private readonly ApplicationDbContext _context;
    private readonly IStorageService _blobService;

    public FileService(ApplicationDbContext context, IStorageService blobService)
    {
        _context = context;
        _blobService = blobService;
    }

    public async Task<IEnumerable<FileMetadataDto>> GetAllFilesAsync()
    {
        var files = await _context.FileMetadata
            .Include(f => f.FileAssignments)
            .Include(f => f.Tags)
            .ToListAsync();

        return files.Select(f => MapToDto(f));
    }

    public async Task<FileMetadataDto?> GetFileByIdAsync(int fileId, int? userId = null)
    {
        var file = await _context.FileMetadata
            .Include(f => f.FileAssignments)
            .Include(f => f.Tags)
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (file == null) return null;

        var assignment = userId.HasValue
            ? file.FileAssignments.FirstOrDefault(fa => fa.UserId == userId.Value)
            : null;

        return MapToDto(file, assignment);
    }

    public async Task<FileMetadataDto?> GetFileByBlobNameAsync(string blobName)
    {
        var file = await _context.FileMetadata
            .Include(f => f.FileAssignments)
            .Include(f => f.Tags)
            .FirstOrDefaultAsync(f => f.BlobName == blobName);

        return file != null ? MapToDto(file) : null;
    }

    public async Task<FileMetadataDto> CreateFileMetadataAsync(CreateFileMetadataRequest request)
    {
        var file = new FileMetadata
        {
            FileName = request.FileName,
            FileUrl = request.FileUrl,
            BlobName = request.BlobName,
            FileSize = request.FileSize,
            ContentType = request.ContentType,
            UploadedAt = DateTime.UtcNow,
            Status = FileTaggingStatus.Unassigned
        };

        _context.FileMetadata.Add(file);
        await _context.SaveChangesAsync();

        return MapToDto(file);
    }

    public async Task<FileMetadataDto> ImportFileFromBlobAsync(BlobFileDto blobFile)
    {
        var existingFile = await _context.FileMetadata
            .FirstOrDefaultAsync(f => f.BlobName == blobFile.BlobName);

        if (existingFile != null)
        {
            return MapToDto(existingFile);
        }

        var file = new FileMetadata
        {
            FileName = blobFile.BlobName,
            FileUrl = blobFile.FileUrl,
            BlobName = blobFile.BlobName,
            FileSize = blobFile.FileSize,
            ContentType = blobFile.ContentType,
            UploadedAt = blobFile.LastModified ?? DateTime.UtcNow,
            Status = FileTaggingStatus.Unassigned
        };

        _context.FileMetadata.Add(file);
        await _context.SaveChangesAsync();

        return MapToDto(file);
    }

    public async Task<int> SyncFilesFromBlobStorageAsync()
    {
        var blobs = await _blobService.ListBlobsAsync();
        var blobNames = blobs.Select(b => b.BlobName).ToHashSet();
        var importedCount = 0;

        await _context.FileMetadata
            .Where(f => !blobNames.Contains(f.BlobName))
            .ExecuteDeleteAsync();

        foreach (var blob in blobs)
        {
            var existingFile = await _context.FileMetadata
                .FirstOrDefaultAsync(f => f.BlobName == blob.BlobName);

            if (existingFile == null)
            {
                var file = new FileMetadata
                {
                    FileName = blob.BlobName,
                    FileUrl = blob.FileUrl,
                    BlobName = blob.BlobName,
                    FileSize = blob.FileSize,
                    ContentType = blob.ContentType,
                    UploadedAt = blob.LastModified ?? DateTime.UtcNow,
                    Status = FileTaggingStatus.Unassigned
                };

                _context.FileMetadata.Add(file);
                importedCount++;
            }
        }

        if (importedCount > 0)
        {
            await _context.SaveChangesAsync();
        }

        return importedCount;
    }

    public async Task<bool> AssignFileToUserAsync(int fileId, int userId, int adminId)
    {
        var file = await _context.FileMetadata.FindAsync(fileId);
        var user = await _context.Users.FindAsync(userId);

        if (file == null || user == null || user.Role != UserRoles.Tagger)
        {
            return false;
        }

        var existingAssignment = await _context.FileAssignments
            .FirstOrDefaultAsync(fa => fa.FileMetadataId == fileId && fa.UserId == userId);

        if (existingAssignment != null)
        {
            return false;
        }

        var assignment = new FileAssignment
        {
            FileMetadataId = fileId,
            UserId = userId,
            AssignedByUserId = adminId,
            AssignedAt = DateTime.UtcNow,
            IsCompleted = false
        };

        _context.FileAssignments.Add(assignment);

        if (file.Status == FileTaggingStatus.Unassigned)
        {
            file.Status = FileTaggingStatus.Assigned;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AssignBlobFileToUserAsync(string blobName, int userId, int adminId)
    {
        var file = await _context.FileMetadata
            .FirstOrDefaultAsync(f => f.BlobName == blobName);

        if (file == null)
        {
            var blobs = await _blobService.ListBlobsAsync();
            var blobFile = blobs.FirstOrDefault(b => b.BlobName == blobName);

            if (blobFile == null)
            {
                return false;
            }

            var importedFile = await ImportFileFromBlobAsync(blobFile);
            file = await _context.FileMetadata.FirstOrDefaultAsync(f => f.BlobName == blobName);

            if (file == null)
            {
                return false;
            }
        }

        return await AssignFileToUserAsync(file.Id, userId, adminId);
    }

    public async Task<AssignMultipleFilesResult> AssignMultipleFilesToUserAsync(List<string> blobNames, int userId, int adminId)
    {
        var result = new AssignMultipleFilesResult
        {
            TotalRequested = blobNames.Count
        };

        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Role != UserRoles.Tagger)
        {
            result.Failed = blobNames.Count;
            result.FailedBlobNames = blobNames;
            return result;
        }

        foreach (var blobName in blobNames)
        {
            var file = await _context.FileMetadata
                .FirstOrDefaultAsync(f => f.BlobName == blobName);

            if (file == null)
            {
                var blobs = await _blobService.ListBlobsAsync();
                var blobFile = blobs.FirstOrDefault(b => b.BlobName == blobName);

                if (blobFile == null)
                {
                    result.Failed++;
                    result.FailedBlobNames.Add(blobName);
                    continue;
                }

                var importedFile = await ImportFileFromBlobAsync(blobFile);
                file = await _context.FileMetadata.FirstOrDefaultAsync(f => f.BlobName == blobName);

                if (file == null)
                {
                    result.Failed++;
                    result.FailedBlobNames.Add(blobName);
                    continue;
                }
            }

            var existingAssignment = await _context.FileAssignments
                .FirstOrDefaultAsync(fa => fa.FileMetadataId == file.Id && fa.UserId == userId);

            if (existingAssignment != null)
            {
                result.Failed++;
                result.FailedBlobNames.Add(blobName);
                continue;
            }

            var assignment = new FileAssignment
            {
                FileMetadataId = file.Id,
                UserId = userId,
                AssignedByUserId = adminId,
                AssignedAt = DateTime.UtcNow,
                IsCompleted = false
            };

            _context.FileAssignments.Add(assignment);

            if (file.Status == FileTaggingStatus.Unassigned)
            {
                file.Status = FileTaggingStatus.Assigned;
            }

            result.SuccessfullyAssigned++;
        }

        if (result.SuccessfullyAssigned > 0)
        {
            await _context.SaveChangesAsync();
        }

        return result;
    }

    public async Task<IEnumerable<FileMetadataDto>> GetFilesAssignedToUserAsync(int userId)
    {
        var assignments = await _context.FileAssignments
            .Where(fa => fa.UserId == userId)
            .Include(fa => fa.FileMetadata)
                .ThenInclude(f => f.Tags)
            .Include(fa => fa.FileMetadata)
                .ThenInclude(f => f.FileAssignments)
            .ToListAsync();

        return assignments.Select(fa => MapToDto(fa.FileMetadata, fa)).ToList();
    }

    public async Task<bool> AddTagsToFileAsync(int fileId, int userId, List<TagDto> tags)
    {
        var file = await _context.FileMetadata.FindAsync(fileId);

        if (file == null)
        {
            return false;
        }

        var assignment = await _context.FileAssignments
            .FirstOrDefaultAsync(fa => fa.FileMetadataId == fileId && fa.UserId == userId);

        if (assignment == null)
        {
            return false;
        }

        var existingTags = await _context.FileTags
            .Where(t => t.FileMetadataId == fileId)
            .ToListAsync();

        foreach (var tag in tags)
        {
            var existing = existingTags.FirstOrDefault(t => t.TagKey == tag.TagKey);
            if (existing != null)
            {
                existing.TagValue = tag.TagValue;
            }
            else
            {
                _context.FileTags.Add(new FileTag
                {
                    FileMetadataId = fileId,
                    TagKey = tag.TagKey,
                    TagValue = tag.TagValue,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        if (file.Status == FileTaggingStatus.Assigned || file.Status == FileTaggingStatus.SendBackToTagger)
        {
            file.Status = FileTaggingStatus.SubmittedToSupervisor;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CompleteFileTaggingAsync(int fileId, int userId)
    {
        var file = await _context.FileMetadata.FindAsync(fileId);

        if (file == null)
        {
            return false;
        }

        var assignment = await _context.FileAssignments
            .FirstOrDefaultAsync(fa => fa.FileMetadataId == fileId && fa.UserId == userId);

        if (assignment == null)
        {
            return false;
        }

        assignment.IsCompleted = true;
        assignment.CompletedAt = DateTime.UtcNow;
        file.Status = FileTaggingStatus.SubmittedToSupervisor;
        file.TaggingCompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<TaggingProgressDto>> GetTaggingProgressAsync()
    {
        var assignments = await _context.FileAssignments
            .Include(fa => fa.User)
            .Include(fa => fa.FileMetadata)
            .ToListAsync();

        var progress = assignments
            .GroupBy(fa => fa.User)
            .Select(g => new TaggingProgressDto
            {
                UserId = g.Key.Id,
                Username = g.Key.Username,
                TotalAssigned = g.Count(),
                TotalInProgress = g.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.Assigned),
                TotalSubmitted = g.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.SubmittedToSupervisor),
                TotalSentBack = g.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.SendBackToTagger),
                TotalApproved = g.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.ApprovedBySupervisor),
                CompletedFiles = g.Where(fa => fa.IsCompleted)
                    .Select(fa => new CompletedFileDto
                    {
                        FileId = fa.FileMetadataId,
                        FileName = fa.FileMetadata.FileName,
                        CompletedAt = fa.CompletedAt
                    })
                    .ToList()
            })
            .ToList();

        return progress;
    }

    public async Task<IEnumerable<FileMetadataDto>> GetUnassignedFilesAsync()
    {
        var files = await _context.FileMetadata
            .Where(f => f.Status == FileTaggingStatus.Unassigned)
            .Include(f => f.Tags)
            .ToListAsync();

        return files.Select(f => MapToDto(f));
    }

    public async Task<bool> UpdateAudioMetadataAsync(int fileId, double durationSeconds)
    {
        var file = await _context.FileMetadata.FindAsync(fileId);

        if (file == null)
        {
            return false;
        }

        if (!FileCategoryHelper.IsAudio(file.ContentType, file.FileName))
        {
            return false;
        }

        file.DurationSeconds = durationSeconds;
        await _context.SaveChangesAsync();
        return true;
    }

    private static FileMetadataDto MapToDto(FileMetadata file, FileAssignment? assignment = null)
    {
        var dto = new FileMetadataDto
        {
            Id = file.Id,
            FileName = file.FileName,
            FileUrl = file.FileUrl,
            BlobName = file.BlobName,
            FileSize = file.FileSize,
            ContentType = file.ContentType,
            FileCategory = FileCategoryHelper.FromContentType(file.ContentType, file.FileName).ToString(),
            UploadedAt = file.UploadedAt,
            Status = file.Status.ToString(),
            TaggingCompletedAt = file.TaggingCompletedAt,
            DurationSeconds = file.DurationSeconds,
            Tags = file.Tags.Select(t => new TagDto
            {
                TagKey = t.TagKey,
                TagValue = t.TagValue
            }).ToList(),
            AssignedToUserIds = file.FileAssignments.Select(fa => fa.UserId).ToList()
        };

        if (assignment != null && file.Status == FileTaggingStatus.SendBackToTagger)
        {
            dto.SupervisorNotes = assignment.SupervisorNotes;
            dto.SupervisorCheckedAt = assignment.CheckedAt;
        }

        return dto;
    }
}
