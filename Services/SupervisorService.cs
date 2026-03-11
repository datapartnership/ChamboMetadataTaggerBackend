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

    public async Task<IEnumerable<StudentWithStatsDto>> GetSupervisorStudentsAsync(int supervisorId)
    {
        var students = await _context.StudentSupervisors
            .Where(ss => ss.SupervisorId == supervisorId && ss.IsActive)
            .Include(ss => ss.Student)
                .ThenInclude(s => s.FileAssignments)
                    .ThenInclude(fa => fa.FileMetadata)
                        .ThenInclude(fm => fm.Tags)
            .Select(ss => new StudentWithStatsDto
            {
                StudentId = ss.Student.Id,
                Username = ss.Student.Username,
                Email = ss.Student.Email,
                TotalAssigned = ss.Student.FileAssignments.Count,
                TotalCompleted = ss.Student.FileAssignments.Count(fa => fa.IsCompleted),
                InProgress = ss.Student.FileAssignments.Count(fa => !fa.IsCompleted),
                RecentFiles = ss.Student.FileAssignments
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
                    }).ToList()
            })
            .ToListAsync();

        return students;
    }

    public async Task<IEnumerable<SupervisorReviewDto>> GetStudentFilesForReviewAsync(int supervisorId, int studentId)
    {
        var isAssigned = await _context.StudentSupervisors
            .AnyAsync(ss => ss.SupervisorId == supervisorId && ss.StudentId == studentId && ss.IsActive);

        if (!isAssigned)
        {
            return new List<SupervisorReviewDto>();
        }

        var files = await _context.FileAssignments
            .Where(fa => fa.UserId == studentId)
            .Include(fa => fa.FileMetadata)
                .ThenInclude(fm => fm.Tags)
            .Include(fa => fa.User)
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

        return files;
    }

    public async Task<IEnumerable<SupervisorReviewDto>> GetAllStudentFilesForSupervisorAsync(int supervisorId)
    {
        var studentIds = await _context.StudentSupervisors
            .Where(ss => ss.SupervisorId == supervisorId && ss.IsActive)
            .Select(ss => ss.StudentId)
            .ToListAsync();

        var files = await _context.FileAssignments
            .Where(fa => studentIds.Contains(fa.UserId))
            .Include(fa => fa.FileMetadata)
                .ThenInclude(fm => fm.Tags)
            .Include(fa => fa.User)
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
            .OrderByDescending(f => f.CompletedAt)
            .ToListAsync();

        return files;
    }

    public async Task<IEnumerable<StudentSupervisorDto>> GetAllSupervisorAssignmentsAsync()
    {
        var assignments = await _context.StudentSupervisors
            .Where(ss => ss.IsActive)
            .Include(ss => ss.Student)
            .Include(ss => ss.Supervisor)
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

        return assignments;
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
}
