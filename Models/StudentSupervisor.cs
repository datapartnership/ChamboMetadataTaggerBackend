namespace MetadataTagging.Models;

public class StudentSupervisor
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int SupervisorId { get; set; }
    public int AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public User Student { get; set; } = null!;
    public User Supervisor { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
}
