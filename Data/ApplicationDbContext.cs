using Microsoft.EntityFrameworkCore;
using MetadataTagging.Models;

namespace MetadataTagging.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<FileMetadata> FileMetadata { get; set; }
    public DbSet<FileAssignment> FileAssignments { get; set; }
    public DbSet<FileTag> FileTags { get; set; }
    public DbSet<StudentSupervisor> StudentSupervisors { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Role).HasMaxLength(50);
        });

        modelBuilder.Entity<FileMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FileName);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.FileUrl).HasMaxLength(2000);
            entity.Property(e => e.BlobName).HasMaxLength(500);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.DurationSeconds).IsRequired(false);
        });

        modelBuilder.Entity<FileAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FileMetadataId, e.UserId });
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsCompleted);

            entity.HasOne(e => e.FileMetadata)
                .WithMany(f => f.FileAssignments)
                .HasForeignKey(e => e.FileMetadataId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.FileAssignments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FileMetadataId);
            entity.HasIndex(e => e.TagKey);
            entity.Property(e => e.TagKey).HasMaxLength(200);
            entity.Property(e => e.TagValue).HasMaxLength(1000);

            entity.HasOne(e => e.FileMetadata)
                .WithMany(f => f.Tags)
                .HasForeignKey(e => e.FileMetadataId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudentSupervisor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.StudentId, e.SupervisorId });
            entity.HasIndex(e => e.SupervisorId);
            entity.HasIndex(e => e.IsActive);

            entity.HasOne(e => e.Student)
                .WithMany()
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Supervisor)
                .WithMany()
                .HasForeignKey(e => e.SupervisorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.AssignedByUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
    }
}
