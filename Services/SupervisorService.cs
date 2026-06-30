using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

public class SupervisorService : ISupervisorService
{
    private readonly ApplicationDbContext _context;

    public SupervisorService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> AssignStudentToSupervisorAsync(int studentId, int supervisorId, int adminId)
    {
        var student = await _context.Users.FindAsync(studentId);
        var supervisor = await _context.Users.FindAsync(supervisorId);

        if (student == null || student.Role != UserRoles.Tagger)
        {
            return false;
        }

        if (supervisor == null || supervisor.Role != UserRoles.Supervisor)
        {
            return false;
        }

        var existingAssignment = await _context.StudentSupervisors
            .FirstOrDefaultAsync(ss => ss.StudentId == studentId && ss.SupervisorId == supervisorId && ss.IsActive);

        if (existingAssignment != null)
        {
            return false;
        }

        var assignment = new StudentSupervisor
        {
            StudentId = studentId,
            SupervisorId = supervisorId,
            AssignedByUserId = adminId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.StudentSupervisors.Add(assignment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnassignStudentFromSupervisorAsync(int studentId, int supervisorId)
    {
        var assignment = await _context.StudentSupervisors
            .FirstOrDefaultAsync(ss => ss.StudentId == studentId && ss.SupervisorId == supervisorId && ss.IsActive);

        if (assignment == null)
        {
            return false;
        }

        assignment.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<PagedResponse<StudentWithStatsDto>> GetSupervisorStudentsAsync(int supervisorId, PaginationParams pagination)
    {
        // Phase 1: paginated students with SQL-computed counts
        var baseQuery = _context.StudentSupervisors
            .Where(ss => ss.SupervisorId == supervisorId && ss.IsActive)
            .Select(ss => new
            {
                StudentId = ss.Student.Id,
                Username = ss.Student.Username,
                Email = ss.Student.Email,
                TotalAssigned = ss.Student.FileAssignments.Count(),
                TotalCompleted = ss.Student.FileAssignments.Count(fa => fa.IsCompleted),
                InProgress = ss.Student.FileAssignments.Count(fa => !fa.IsCompleted)
            });

        var orderedQuery = pagination.SortBy?.ToLowerInvariant() switch
        {
            "username" => pagination.IsDescending
                ? baseQuery.OrderByDescending(s => s.Username)
                : baseQuery.OrderBy(s => s.Username),
            "email" => pagination.IsDescending
                ? baseQuery.OrderByDescending(s => s.Email)
                : baseQuery.OrderBy(s => s.Email),
            "totalassigned" => pagination.IsDescending
                ? baseQuery.OrderByDescending(s => s.TotalAssigned)
                : baseQuery.OrderBy(s => s.TotalAssigned),
            "totalcompleted" => pagination.IsDescending
                ? baseQuery.OrderByDescending(s => s.TotalCompleted)
                : baseQuery.OrderBy(s => s.TotalCompleted),
            _ => baseQuery.OrderBy(s => s.Username)
        };

        var totalCount = await orderedQuery.CountAsync();

        var pagedStudents = await orderedQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        // Phase 2: recent files for only the students on this page
        var studentIds = pagedStudents.Select(s => s.StudentId).ToList();

        var recentAssignments = await _context.FileAssignments
            .Where(fa => studentIds.Contains(fa.UserId))
            .Include(fa => fa.FileMetadata)
                .ThenInclude(fm => fm.Tags)
            .ToListAsync();

        var recentFilesGrouped = recentAssignments
            .GroupBy(fa => fa.UserId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(fa => fa.AssignedAt)
                .Take(5)
                .Select(fa => new FileMetadataDto
                {
                    Id = fa.FileMetadata.Id,
                    FileName = fa.FileMetadata.FileName,
                    FileUrl = fa.FileMetadata.FileUrl,
                    BlobName = fa.FileMetadata.BlobName,
                    FileSize = fa.FileMetadata.FileSize,
                    ContentType = fa.FileMetadata.ContentType,
                    UploadedAt = fa.FileMetadata.UploadedAt,
                    Status = fa.FileMetadata.Status.ToString(),
                    TaggingCompletedAt = fa.FileMetadata.TaggingCompletedAt,
                    Tags = fa.FileMetadata.Tags.Select(t => new TagDto
                    {
                        TagKey = t.TagKey,
                        TagValue = t.TagValue
                    }).ToList(),
                    AssignedToUserIds = new List<int> { fa.UserId }
                })
                .ToList());

        return new PagedResponse<StudentWithStatsDto>
        {
            Items = pagedStudents.Select(s => new StudentWithStatsDto
            {
                StudentId = s.StudentId,
                Username = s.Username,
                Email = s.Email,
                TotalAssigned = s.TotalAssigned,
                TotalCompleted = s.TotalCompleted,
                InProgress = s.InProgress,
                RecentFiles = recentFilesGrouped.GetValueOrDefault(s.StudentId) ?? new List<FileMetadataDto>()
            }),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResponse<SupervisorReviewDto>> GetStudentFilesForReviewAsync(int supervisorId, int studentId, PaginationParams pagination)
    {
        var isAssigned = await _context.StudentSupervisors
            .AnyAsync(ss => ss.SupervisorId == supervisorId && ss.StudentId == studentId && ss.IsActive);

        if (!isAssigned)
        {
            return new PagedResponse<SupervisorReviewDto>
            {
                Items = new List<SupervisorReviewDto>(),
                Page = pagination.Page,
                PageSize = pagination.PageSize,
                TotalCount = 0
            };
        }

        IQueryable<FileAssignment> baseQuery = _context.FileAssignments
            .Where(fa => fa.UserId == studentId);

        IQueryable<FileAssignment> sortedQuery = pagination.SortBy?.ToLowerInvariant() switch
        {
            "filename" => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.FileMetadata.FileName)
                : baseQuery.OrderBy(fa => fa.FileMetadata.FileName),
            "status" => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.FileMetadata.Status)
                : baseQuery.OrderBy(fa => fa.FileMetadata.Status),
            "completedat" => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.CompletedAt)
                : baseQuery.OrderBy(fa => fa.CompletedAt),
            _ => baseQuery.OrderBy(fa => fa.FileMetadata.FileName)
        };

        var totalCount = await sortedQuery.CountAsync();

        var files = await sortedQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(fa => new SupervisorReviewDto
            {
                FileId = fa.FileMetadata.Id,
                FileName = fa.FileMetadata.FileName,
                Status = fa.FileMetadata.Status.ToString(),
                StudentId = fa.User.Id,
                StudentUsername = fa.User.Username,
                Tags = fa.FileMetadata.Tags.Select(t => new TagDto
                {
                    TagKey = t.TagKey,
                    TagValue = t.TagValue
                }).ToList(),
                CompletedAt = fa.CompletedAt,
                FileUrl = fa.FileMetadata.FileUrl,
                BlobName = fa.FileMetadata.BlobName,
                IsCheckedBySupervisor = fa.IsCheckedBySupervisor,
                CheckedBySupervisorId = fa.CheckedBySupervisorId,
                CheckedAt = fa.CheckedAt,
                SupervisorNotes = fa.SupervisorNotes
            })
            .ToListAsync();

        return new PagedResponse<SupervisorReviewDto>
        {
            Items = files,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResponse<SupervisorReviewDto>> GetAllStudentFilesForSupervisorAsync(int supervisorId, PaginationParams pagination)
    {
        IQueryable<FileAssignment> baseQuery = _context.FileAssignments
            .Where(fa => _context.StudentSupervisors
                .Any(ss => ss.SupervisorId == supervisorId && ss.IsActive && ss.StudentId == fa.UserId));

        IQueryable<FileAssignment> sortedQuery = pagination.SortBy?.ToLowerInvariant() switch
        {
            "filename" => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.FileMetadata.FileName)
                : baseQuery.OrderBy(fa => fa.FileMetadata.FileName),
            "status" => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.FileMetadata.Status)
                : baseQuery.OrderBy(fa => fa.FileMetadata.Status),
            "studentusername" => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.User.Username)
                : baseQuery.OrderBy(fa => fa.User.Username),
            _ => pagination.IsDescending
                ? baseQuery.OrderByDescending(fa => fa.CompletedAt)
                : baseQuery.OrderBy(fa => fa.CompletedAt),
        };

        var totalCount = await sortedQuery.CountAsync();

        var files = await sortedQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(fa => new SupervisorReviewDto
            {
                FileId = fa.FileMetadata.Id,
                FileName = fa.FileMetadata.FileName,
                Status = fa.FileMetadata.Status.ToString(),
                StudentId = fa.User.Id,
                StudentUsername = fa.User.Username,
                Tags = fa.FileMetadata.Tags.Select(t => new TagDto
                {
                    TagKey = t.TagKey,
                    TagValue = t.TagValue
                }).ToList(),
                CompletedAt = fa.CompletedAt,
                FileUrl = fa.FileMetadata.FileUrl,
                BlobName = fa.FileMetadata.BlobName,
                IsCheckedBySupervisor = fa.IsCheckedBySupervisor,
                CheckedBySupervisorId = fa.CheckedBySupervisorId,
                CheckedAt = fa.CheckedAt,
                SupervisorNotes = fa.SupervisorNotes
            })
            .ToListAsync();

        return new PagedResponse<SupervisorReviewDto>
        {
            Items = files,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResponse<StudentSupervisorDto>> GetAllSupervisorAssignmentsAsync(PaginationParams pagination)
    {
        IQueryable<StudentSupervisor> baseQuery = _context.StudentSupervisors
            .Where(ss => ss.IsActive);

        IQueryable<StudentSupervisor> sortedQuery = pagination.SortBy?.ToLowerInvariant() switch
        {
            "studentusername" => pagination.IsDescending
                ? baseQuery.OrderByDescending(ss => ss.Student.Username)
                : baseQuery.OrderBy(ss => ss.Student.Username),
            "supervisorusername" => pagination.IsDescending
                ? baseQuery.OrderByDescending(ss => ss.Supervisor.Username)
                : baseQuery.OrderBy(ss => ss.Supervisor.Username),
            "assignedat" => pagination.IsDescending
                ? baseQuery.OrderByDescending(ss => ss.AssignedAt)
                : baseQuery.OrderBy(ss => ss.AssignedAt),
            _ => baseQuery.OrderBy(ss => ss.Student.Username)
        };

        var totalCount = await sortedQuery.CountAsync();

        var assignments = await sortedQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(ss => new StudentSupervisorDto
            {
                Id = ss.Id,
                StudentId = ss.Student.Id,
                StudentUsername = ss.Student.Username,
                StudentEmail = ss.Student.Email,
                SupervisorId = ss.Supervisor.Id,
                SupervisorUsername = ss.Supervisor.Username,
                AssignedAt = ss.AssignedAt,
                IsActive = ss.IsActive
            })
            .ToListAsync();

        return new PagedResponse<StudentSupervisorDto>
        {
            Items = assignments,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<bool> CanSupervisorAccessFileAsync(int supervisorId, int fileId)
    {
        return await _context.FileAssignments
            .AnyAsync(fa => fa.FileMetadataId == fileId &&
                _context.StudentSupervisors.Any(ss =>
                    ss.SupervisorId == supervisorId &&
                    ss.IsActive &&
                    ss.StudentId == fa.UserId));
    }

    public async Task<bool> MarkFileAsCheckedAsync(int fileId, int studentId, int supervisorId, string? notes)
    {
        var isAssigned = await _context.StudentSupervisors
            .AnyAsync(ss => ss.SupervisorId == supervisorId && ss.StudentId == studentId && ss.IsActive);

        if (!isAssigned)
        {
            return false;
        }

        var assignment = await _context.FileAssignments
            .FirstOrDefaultAsync(fa => fa.FileMetadataId == fileId && fa.UserId == studentId);

        if (assignment == null)
        {
            return false;
        }

        assignment.IsCheckedBySupervisor = true;
        assignment.CheckedBySupervisorId = supervisorId;
        assignment.CheckedAt = DateTime.UtcNow;
        assignment.SupervisorNotes = notes;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SendBackToTaggerAsync(int fileId, int studentId, int supervisorId, string? notes)
    {
        var isAssigned = await _context.StudentSupervisors
            .AnyAsync(ss => ss.SupervisorId == supervisorId && ss.StudentId == studentId && ss.IsActive);

        if (!isAssigned)
        {
            return false;
        }

        var assignment = await _context.FileAssignments
            .FirstOrDefaultAsync(fa => fa.FileMetadataId == fileId && fa.UserId == studentId);

        if (assignment == null)
        {
            return false;
        }

        var file = await _context.FileMetadata.FindAsync(fileId);

        if (file == null)
        {
            return false;
        }

        assignment.IsCompleted = false;
        assignment.CompletedAt = null;
        assignment.IsCheckedBySupervisor = false;
        assignment.CheckedBySupervisorId = supervisorId;
        assignment.CheckedAt = DateTime.UtcNow;
        assignment.SupervisorNotes = notes;

        file.Status = FileTaggingStatus.SendBackToTagger;
        file.TaggingCompletedAt = null;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> EditFileTagsAsync(int fileId, int studentId, int supervisorId, List<TagDto> tags, string? notes)
    {
        var isAssigned = await _context.StudentSupervisors
            .AnyAsync(ss => ss.SupervisorId == supervisorId && ss.StudentId == studentId && ss.IsActive);

        if (!isAssigned)
        {
            return false;
        }

        var assignment = await _context.FileAssignments
            .FirstOrDefaultAsync(fa => fa.FileMetadataId == fileId && fa.UserId == studentId);

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
            // Tags with keys not already present for this file are ignored —
            // supervisors may only edit existing tag values, not define new tags.
        }

        assignment.IsCheckedBySupervisor = true;
        assignment.CheckedBySupervisorId = supervisorId;
        assignment.CheckedAt = DateTime.UtcNow;
        assignment.SupervisorNotes = notes;

        await _context.SaveChangesAsync();
        return true;
    }
}
