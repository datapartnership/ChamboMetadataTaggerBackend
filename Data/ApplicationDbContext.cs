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

    // ── Catalog API entities (ported from the LRLL merge) ──────────────────────
    // Collections and their files are rebuilt from catalog storage by POST /api/seed.
    public DbSet<Collection> Collections { get; set; }
    public DbSet<CollectionFile> CollectionFiles { get; set; }
    public DbSet<Keyword> Keywords { get; set; }
    public DbSet<Theme> Themes { get; set; }
    public DbSet<FileDownloadLog> FileDownloadLogs { get; set; }
    public DbSet<DatasetAccessApplication> DatasetAccessApplications { get; set; }

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
            entity.HasIndex(e => e.BlobName);
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

        // ── Catalog API entities (ported from the LRLL merge) ───────────────────
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.Property(c => c.MediaType).HasConversion<string>().HasMaxLength(20);
            entity.HasMany(c => c.Files)
                .WithOne(f => f.Collection)
                .HasForeignKey(f => f.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(c => c.MediaType).HasDatabaseName("idx_collections_mediatype");
            entity.HasIndex(c => c.Provider).HasDatabaseName("idx_collections_provider");
            entity.HasIndex(c => c.LanguageCode).HasDatabaseName("idx_collections_language");
        });

        modelBuilder.Entity<CollectionFile>(entity =>
        {
            entity.Property(f => f.Kind).HasConversion<string>().HasMaxLength(20);
            entity.HasMany(f => f.Keywords)
                .WithOne(k => k.File)
                .HasForeignKey(k => k.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(f => f.Themes)
                .WithOne(t => t.File)
                .HasForeignKey(t => t.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(f => f.ContentUrl).IsUnique().HasDatabaseName("idx_collectionfiles_contenturl");
            entity.HasIndex(f => f.CollectionId).HasDatabaseName("idx_collectionfiles_collection");
            entity.HasIndex(f => f.Kind).HasDatabaseName("idx_collectionfiles_kind");
            entity.HasIndex(f => f.Author).HasDatabaseName("idx_collectionfiles_author");
        });

        modelBuilder.Entity<Keyword>()
            .HasIndex(k => k.Value).HasDatabaseName("idx_keywords_value");
        modelBuilder.Entity<Keyword>()
            .HasIndex(k => k.FileId).HasDatabaseName("idx_keywords_file");

        modelBuilder.Entity<Theme>()
            .HasIndex(t => t.Value).HasDatabaseName("idx_themes_value");
        modelBuilder.Entity<Theme>()
            .HasIndex(t => t.FileId).HasDatabaseName("idx_themes_file");

        // ── Rate limiting ─────────────────────────────────────────────────────
        modelBuilder.Entity<FileDownloadLog>()
            .HasKey(l => new { l.UserId, l.Date });

        modelBuilder.Entity<DatasetAccessApplication>(entity =>
        {
            entity.Property(a => a.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(a => a.LastName).IsRequired().HasMaxLength(100);
            entity.Property(a => a.Organization).IsRequired().HasMaxLength(200);
            entity.Property(a => a.OrganizationEmail).IsRequired().HasMaxLength(254);
            entity.Property(a => a.JobTitle).IsRequired().HasMaxLength(200);
            entity.Property(a => a.ProjectDescription).IsRequired().HasMaxLength(5000);
            entity.Property(a => a.EthicalDataHandling).IsRequired().HasMaxLength(5000);
            entity.HasIndex(a => a.SubmittedAtUtc)
                .IsDescending()
                .HasDatabaseName("idx_datasetaccessapplications_submitted");
        });

        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
    }
}
