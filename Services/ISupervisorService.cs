using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

public interface ISupervisorService
{
    Task<bool> AssignStudentToSupervisorAsync(int studentId, int supervisorId, int adminId);
    Task<bool> UnassignStudentFromSupervisorAsync(int studentId, int supervisorId);
    Task<IEnumerable<StudentWithStatsDto>> GetSupervisorStudentsAsync(int supervisorId);
    Task<IEnumerable<SupervisorReviewDto>> GetStudentFilesForReviewAsync(int supervisorId, int studentId);
    Task<IEnumerable<SupervisorReviewDto>> GetAllStudentFilesForSupervisorAsync(int supervisorId);
    Task<IEnumerable<StudentSupervisorDto>> GetAllSupervisorAssignmentsAsync();
    Task<bool> MarkFileAsCheckedAsync(int fileId, int studentId, int supervisorId, string? notes);
    Task<bool> SendBackToTaggerAsync(int fileId, int studentId, int supervisorId, string? notes);
    Task<bool> EditFileTagsAsync(int fileId, int studentId, int supervisorId, List<TagDto> tags, string? notes);
}
