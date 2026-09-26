using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>
/// Imports files, tags and tagging assignments from a plain-SQL pg_dump of the
/// pre-migrations database. The old users are not imported: every tag and assignment is
/// attributed to the tagger, and every assignment/approval to the approver. Both users are
/// created if missing, so their first Entra ID sign-in (matched by email) takes them over.
/// Existing files, assignments and tags are replaced; users and supervisor links are kept.
/// Blob names are normalized to catalog keys, and the resulting tags are published to any
/// catalog files already seeded.
/// </summary>
public class TaggingImportService(
    ApplicationDbContext context, CatalogTagPublisher publisher, ILogger<TaggingImportService> logger)
{
    private const string FileMetadataTable = "FileMetadata";
    private const string FileAssignmentsTable = "FileAssignments";
    private const string FileTagsTable = "FileTags";

    public async Task<TaggingImportResult> ImportAsync(
        Stream dump, string taggerEmail, string approverEmail, CancellationToken ct = default)
    {
        var tables = await PgDumpCopyReader.ReadAsync(
            dump, [FileMetadataTable, FileAssignmentsTable, FileTagsTable], ct);
        foreach (var name in new[] { FileMetadataTable, FileAssignmentsTable, FileTagsTable })
        {
            if (!tables.ContainsKey(name))
                throw new FormatException($"The dump contains no data for the {name} table.");
        }

        var files = ReadFiles(tables[FileMetadataTable]);
        var assignmentsInDump = ReadAssignments(tables[FileAssignmentsTable]);
        var tags = ReadTags(tables[FileTagsTable]);

        var fileIds = files.Select(f => f.Id).ToHashSet();
        var orphanedAssignments = assignmentsInDump.Count(a => !fileIds.Contains(a.FileMetadataId));
        var orphanedTags = tags.Count(t => !fileIds.Contains(t.FileMetadataId));
        if (orphanedAssignments > 0 || orphanedTags > 0)
        {
            logger.LogWarning("Skipping {Assignments} assignments and {Tags} tags that reference files missing from the dump.",
                orphanedAssignments, orphanedTags);
        }

        // With everyone merged into one tagger, keep a single assignment per file: approved
        // first, then completed, then the most recent.
        var assignments = assignmentsInDump
            .Where(a => fileIds.Contains(a.FileMetadataId))
            .GroupBy(a => a.FileMetadataId)
            .Select(g => g
                .OrderByDescending(a => a.IsCheckedBySupervisor)
                .ThenByDescending(a => a.IsCompleted)
                .ThenByDescending(a => a.AssignedAt)
                .ThenByDescending(a => a.Id)
                .First())
            .ToList();
        tags = tags.Where(t => fileIds.Contains(t.FileMetadataId)).ToList();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var tagger = await EnsureUserAsync(taggerEmail, UserRoles.Tagger, ct);
        var approver = await EnsureUserAsync(approverEmail, UserRoles.Admin, ct);

        foreach (var a in assignments)
        {
            a.UserId = tagger.Id;
            a.AssignedByUserId = approver.Id;
            a.CheckedBySupervisorId = a.IsCheckedBySupervisor || a.CheckedBySupervisorId != null ? approver.Id : null;
        }
        foreach (var t in tags)
            t.CreatedByUserId = tagger.Id;

        // Catalog files tagged before the import must lose tags the dump does not carry.
        var affectedKeys = await context.FileMetadata
            .Where(f => f.BlobName != null)
            .Select(f => f.BlobName!)
            .ToListAsync(ct);
        affectedKeys.AddRange(files.Select(f => f.BlobName).OfType<string>());

        await context.FileTags.ExecuteDeleteAsync(ct);
        await context.FileAssignments.ExecuteDeleteAsync(ct);
        await context.FileMetadata.ExecuteDeleteAsync(ct);

        context.FileMetadata.AddRange(files);
        context.FileAssignments.AddRange(assignments);
        context.FileTags.AddRange(tags);
        await context.SaveChangesAsync(ct);

        if (context.Database.IsNpgsql())
        {
            // Rows were inserted with their original ids, so move each identity past them.
            await context.Database.ExecuteSqlRawAsync("""
                SELECT setval(pg_get_serial_sequence('public."FileMetadata"', 'Id'), COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL) FROM public."FileMetadata";
                SELECT setval(pg_get_serial_sequence('public."FileAssignments"', 'Id'), COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL) FROM public."FileAssignments";
                SELECT setval(pg_get_serial_sequence('public."FileTags"', 'Id'), COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL) FROM public."FileTags";
                """, ct);
        }

        await transaction.CommitAsync(ct);
        context.ChangeTracker.Clear();

        await publisher.ApplyAsync(affectedKeys, ct);
        context.ChangeTracker.Clear();

        logger.LogInformation(
            "Imported {Files} files, {Assignments} assignments ({InDump} in dump) and {Tags} tags for tagger {Tagger} / approver {Approver}.",
            files.Count, assignments.Count, assignmentsInDump.Count, tags.Count, tagger.Email, approver.Email);

        return new TaggingImportResult
        {
            TaggerUserId = tagger.Id,
            ApproverUserId = approver.Id,
            FilesImported = files.Count,
            AssignmentsInDump = assignmentsInDump.Count,
            AssignmentsImported = assignments.Count,
            TagsImported = tags.Count,
        };
    }

    private async Task<User> EnsureUserAsync(string email, string role, CancellationToken ct)
    {
        var normalized = email.Trim();
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized.ToLower(), ct);
        if (user != null)
            return user;

        user = new User
        {
            Username = normalized,
            Email = normalized,
            PasswordHash = null,
            Role = role,
            AuthProvider = UserAuthProviders.Entra,
            IsActive = true,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync(ct);
        return user;
    }

    private static List<FileMetadata> ReadFiles(CopyTable t) => t.Rows.Select(r => new FileMetadata
    {
        Id = t.Int(r, "Id"),
        FileName = t.Required(r, "FileName"),
        FileUrl = t.Required(r, "FileUrl"),
        BlobName = t.Get(r, "BlobName") is { } blobName ? CatalogKey.Normalize(blobName) : null,
        FileSize = t.Long(r, "FileSize"),
        ContentType = t.Get(r, "ContentType"),
        UploadedAt = t.Timestamp(r, "UploadedAt"),
        Status = (FileTaggingStatus)t.Int(r, "Status"),
        TaggingCompletedAt = t.NullableTimestamp(r, "TaggingCompletedAt"),
        DurationSeconds = t.NullableDouble(r, "DurationSeconds"),
    }).ToList();

    private static List<FileAssignment> ReadAssignments(CopyTable t) => t.Rows.Select(r => new FileAssignment
    {
        Id = t.Int(r, "Id"),
        FileMetadataId = t.Int(r, "FileMetadataId"),
        UserId = t.Int(r, "UserId"),
        AssignedAt = t.Timestamp(r, "AssignedAt"),
        AssignedByUserId = t.NullableInt(r, "AssignedByUserId"),
        CompletedAt = t.NullableTimestamp(r, "CompletedAt"),
        IsCompleted = t.Bool(r, "IsCompleted"),
        IsCheckedBySupervisor = t.Bool(r, "IsCheckedBySupervisor"),
        CheckedBySupervisorId = t.NullableInt(r, "CheckedBySupervisorId"),
        CheckedAt = t.NullableTimestamp(r, "CheckedAt"),
        SupervisorNotes = t.Get(r, "SupervisorNotes"),
    }).ToList();

    private static List<FileTag> ReadTags(CopyTable t) => t.Rows.Select(r => new FileTag
    {
        Id = t.Int(r, "Id"),
        FileMetadataId = t.Int(r, "FileMetadataId"),
        TagKey = t.Required(r, "TagKey"),
        TagValue = t.Required(r, "TagValue"),
        CreatedByUserId = t.Int(r, "CreatedByUserId"),
        CreatedAt = t.Timestamp(r, "CreatedAt"),
    }).ToList();
}
