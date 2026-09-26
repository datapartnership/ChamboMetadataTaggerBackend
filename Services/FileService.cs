using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public class FileService : IFileService
{
    public const string PreviewUnavailableMessage =
        "Preview URLs are not available for local catalog storage; use the file's /stream endpoint instead.";

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly CatalogTagPublisher _publisher;

    public FileService(ApplicationDbContext context, IFileStorageService storage, CatalogTagPublisher publisher)
    {
        _context = context;
        _storage = storage;
        _publisher = publisher;
    }

    private Task PublishAsync(FileMetadata file) =>
        file.BlobName is null ? Task.CompletedTask : _publisher.ApplyAsync([file.BlobName]);

    public static BlobFileDto ToBlobFile(StorageFileEntry entry)
    {
        var key = CatalogKey.Normalize(entry.Key);
        var contentType = FileCategoryHelper.GetContentType(key);
        return new BlobFileDto
        {
            BlobName = key,
            FileUrl = key,
            FileSize = entry.Size,
            ContentType = contentType,
            FileCategory = FileCategoryHelper.FromContentType(contentType, key).ToString(),
            LastModified = entry.LastModified == default ? null : entry.LastModified.UtcDateTime,
        };
    }

    private async Task<FileMetadata?> FindOrImportAsync(string blobName)
    {
        blobName = CatalogKey.Normalize(blobName);
        var file = await _context.FileMetadata.FirstOrDefaultAsync(f => f.BlobName == blobName);
        if (file != null)
            return file;

        if (!CatalogKey.IsCollectionContent(blobName) || await _storage.GetEntryAsync(blobName) is not { } entry)
            return null;

        await ImportFileFromBlobAsync(ToBlobFile(entry));
        return await _context.FileMetadata.FirstOrDefaultAsync(f => f.BlobName == blobName);
    }

    public async Task<PagedResponse<FileMetadataDto>> GetAssignedFilesAsync(PaginationParams pagination)
    {
        IQueryable<FileMetadata> query = _context.FileMetadata
            .Where(f => f.Status != FileTaggingStatus.Unassigned)
            .Include(f => f.FileAssignments)
            .Include(f => f.Tags);

        query = pagination.SortBy?.ToLowerInvariant() switch
        {
            "filename" => pagination.IsDescending
                ? query.OrderByDescending(f => f.FileName)
                : query.OrderBy(f => f.FileName),
            "filesize" => pagination.IsDescending
                ? query.OrderByDescending(f => f.FileSize)
                : query.OrderBy(f => f.FileSize),
            "status" => pagination.IsDescending
                ? query.OrderByDescending(f => f.Status)
                : query.OrderBy(f => f.Status),
            "contenttype" => pagination.IsDescending
                ? query.OrderByDescending(f => f.ContentType)
                : query.OrderBy(f => f.ContentType),
            "durationseconds" => pagination.IsDescending
                ? query.OrderByDescending(f => f.DurationSeconds)
                : query.OrderBy(f => f.DurationSeconds),
            _ => pagination.IsDescending
                ? query.OrderByDescending(f => f.UploadedAt)
                : query.OrderBy(f => f.UploadedAt),
        };

        var totalCount = await query.CountAsync();

        var files = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResponse<FileMetadataDto>
        {
            Items = files.Select(f => MapToDto(f)),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
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
        blobName = CatalogKey.Normalize(blobName);
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
            BlobName = request.BlobName is null ? null : CatalogKey.Normalize(request.BlobName),
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
        var blobName = CatalogKey.Normalize(blobFile.BlobName);
        var existingFile = await _context.FileMetadata
            .FirstOrDefaultAsync(f => f.BlobName == blobName);

        if (existingFile != null)
        {
            return MapToDto(existingFile);
        }

        var file = NewFileMetadata(blobFile, blobName);
        _context.FileMetadata.Add(file);
        await _context.SaveChangesAsync();

        return MapToDto(file);
    }

    private static FileMetadata NewFileMetadata(BlobFileDto blobFile, string blobName) => new()
    {
        FileName = blobName,
        FileUrl = blobFile.FileUrl,
        BlobName = blobName,
        FileSize = blobFile.FileSize,
        ContentType = blobFile.ContentType,
        UploadedAt = blobFile.LastModified ?? DateTime.UtcNow,
        Status = FileTaggingStatus.Unassigned
    };

    public async Task<SyncBlobFilesResponse> SyncFilesFromBlobStorageAsync()
    {
        var blobs = (await _storage.ListFilesAsync())
            .Where(e => CatalogKey.IsCollectionContent(e.Key))
            .Select(ToBlobFile)
            .ToList();
        var blobNames = blobs.Select(b => b.BlobName).ToHashSet(StringComparer.Ordinal);

        var existing = await _context.FileMetadata
            .Select(f => new { f.Id, f.BlobName })
            .ToListAsync();

        var removed = existing
            .Where(f => f.BlobName == null || !blobNames.Contains(f.BlobName))
            .ToList();
        foreach (var chunk in removed.Select(f => f.Id).Chunk(1000))
        {
            await _context.FileMetadata
                .Where(f => chunk.Contains(f.Id))
                .ExecuteDeleteAsync();
        }
        await _publisher.ApplyAsync(removed.Select(f => f.BlobName).OfType<string>());

        var known = existing
            .Select(f => f.BlobName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var added = blobs.Where(b => !known.Contains(b.BlobName)).ToList();
        _context.FileMetadata.AddRange(added.Select(b => NewFileMetadata(b, b.BlobName)));
        if (added.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        return new SyncBlobFilesResponse
        {
            TotalBlobs = blobs.Count,
            ImportedFiles = added.Count,
            ExistingFiles = blobs.Count - added.Count
        };
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
        var file = await FindOrImportAsync(blobName);
        return file != null && await AssignFileToUserAsync(file.Id, userId, adminId);
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
            var file = await FindOrImportAsync(blobName);

            if (file == null)
            {
                result.Failed++;
                result.FailedBlobNames.Add(blobName);
                continue;
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

    public async Task<PagedResponse<FileMetadataDto>> GetFilesAssignedToUserAsync(int userId, PaginationParams pagination)
    {
        IQueryable<FileAssignment> query = _context.FileAssignments
            .Where(fa => fa.UserId == userId)
            .Include(fa => fa.FileMetadata)
                .ThenInclude(f => f.Tags)
            .Include(fa => fa.FileMetadata)
                .ThenInclude(f => f.FileAssignments);

        query = pagination.SortBy?.ToLowerInvariant() switch
        {
            "filename" => pagination.IsDescending
                ? query.OrderByDescending(fa => fa.FileMetadata.FileName)
                : query.OrderBy(fa => fa.FileMetadata.FileName),
            "filesize" => pagination.IsDescending
                ? query.OrderByDescending(fa => fa.FileMetadata.FileSize)
                : query.OrderBy(fa => fa.FileMetadata.FileSize),
            "status" => pagination.IsDescending
                ? query.OrderByDescending(fa => fa.FileMetadata.Status)
                : query.OrderBy(fa => fa.FileMetadata.Status),
            _ => pagination.IsDescending
                ? query.OrderByDescending(fa => fa.FileMetadata.UploadedAt)
                : query.OrderBy(fa => fa.FileMetadata.UploadedAt),
        };

        var totalCount = await query.CountAsync();

        var assignments = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResponse<FileMetadataDto>
        {
            Items = assignments.Select(fa => MapToDto(fa.FileMetadata, fa)),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
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
        await PublishAsync(file);
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
        await PublishAsync(file);
        return true;
    }

    public async Task<PagedResponse<TaggingProgressDto>> GetTaggingProgressAsync(PaginationParams pagination)
    {
        var userStatsQuery = _context.Users
            .Where(u => u.FileAssignments.Any())
            .Select(u => new
            {
                UserId = u.Id,
                Username = u.Username,
                TotalAssigned = u.FileAssignments.Count(),
                TotalInProgress = u.FileAssignments.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.Assigned),
                TotalSubmitted = u.FileAssignments.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.SubmittedToSupervisor),
                TotalSentBack = u.FileAssignments.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.SendBackToTagger),
                TotalApproved = u.FileAssignments.Count(fa => fa.FileMetadata.Status == FileTaggingStatus.ApprovedBySupervisor)
            });

        userStatsQuery = pagination.SortBy?.ToLowerInvariant() switch
        {
            "username" => pagination.IsDescending
                ? userStatsQuery.OrderByDescending(u => u.Username)
                : userStatsQuery.OrderBy(u => u.Username),
            "totalassigned" => pagination.IsDescending
                ? userStatsQuery.OrderByDescending(u => u.TotalAssigned)
                : userStatsQuery.OrderBy(u => u.TotalAssigned),
            "totalapproved" => pagination.IsDescending
                ? userStatsQuery.OrderByDescending(u => u.TotalApproved)
                : userStatsQuery.OrderBy(u => u.TotalApproved),
            _ => userStatsQuery.OrderBy(u => u.Username)
        };

        var totalCount = await userStatsQuery.CountAsync();

        var userStats = await userStatsQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var userIds = userStats.Select(u => u.UserId).ToList();

        var completedFilesRaw = await _context.FileAssignments
            .Where(fa => userIds.Contains(fa.UserId) && fa.IsCompleted)
            .Select(fa => new { fa.UserId, fa.FileMetadataId, fa.FileMetadata.FileName, fa.CompletedAt })
            .ToListAsync();

        var completedFilesByUser = completedFilesRaw
            .GroupBy(cf => cf.UserId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(cf => cf.CompletedAt)
                .Take(10)
                .Select(cf => new CompletedFileDto
                {
                    FileId = cf.FileMetadataId,
                    FileName = cf.FileName,
                    CompletedAt = cf.CompletedAt
                })
                .ToList());

        return new PagedResponse<TaggingProgressDto>
        {
            Items = userStats.Select(u => new TaggingProgressDto
            {
                UserId = u.UserId,
                Username = u.Username,
                TotalAssigned = u.TotalAssigned,
                TotalInProgress = u.TotalInProgress,
                TotalSubmitted = u.TotalSubmitted,
                TotalSentBack = u.TotalSentBack,
                TotalApproved = u.TotalApproved,
                CompletedFiles = completedFilesByUser.GetValueOrDefault(u.UserId) ?? new List<CompletedFileDto>()
            }),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResponse<FileMetadataDto>> GetUnassignedFilesAsync(PaginationParams pagination)
    {
        IQueryable<FileMetadata> query = _context.FileMetadata
            .Where(f => f.Status == FileTaggingStatus.Unassigned)
            .Include(f => f.Tags);

        query = pagination.SortBy?.ToLowerInvariant() switch
        {
            "filename" => pagination.IsDescending
                ? query.OrderByDescending(f => f.FileName)
                : query.OrderBy(f => f.FileName),
            "filesize" => pagination.IsDescending
                ? query.OrderByDescending(f => f.FileSize)
                : query.OrderBy(f => f.FileSize),
            "contenttype" => pagination.IsDescending
                ? query.OrderByDescending(f => f.ContentType)
                : query.OrderBy(f => f.ContentType),
            _ => pagination.IsDescending
                ? query.OrderByDescending(f => f.UploadedAt)
                : query.OrderBy(f => f.UploadedAt),
        };

        var totalCount = await query.CountAsync();

        var files = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResponse<FileMetadataDto>
        {
            Items = files.Select(f => MapToDto(f)),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
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
