using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

public interface ISupervisorService
{
    Task<bool> AssignStudentToSupervisorAsync(int studentId, int supervisorId, int adminId);
    Task<bool> UnassignStudentFromSupervisorAsync(int studentId, int supervisorId);
    Task<PagedResponse<StudentWithStatsDto>> GetSupervisorStudentsAsync(int supervisorId, PaginationParams pagination);
    Task<PagedResponse<SupervisorReviewDto>> GetStudentFilesForReviewAsync(int supervisorId, int studentId, PaginationParams pagination);
    Task<PagedResponse<SupervisorReviewDto>> GetAllStudentFilesForSupervisorAsync(int supervisorId, PaginationParams pagination);
    Task<PagedResponse<StudentSupervisorDto>> GetAllSupervisorAssignmentsAsync(PaginationParams pagination);
    Task<bool> CanSupervisorAccessFileAsync(int supervisorId, int fileId);
    Task<bool> MarkFileAsCheckedAsync(int fileId, int studentId, int supervisorId, string? notes);
    Task<bool> SendBackToTaggerAsync(int fileId, int studentId, int supervisorId, string? notes);
    Task<bool> EditFileTagsAsync(int fileId, int studentId, int supervisorId, List<TagDto> tags, string? notes);
}
