using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetadataTagging.Tests;

public class FileServiceAssignedCountTests
{
    [Fact]
    public async Task AssignedPlusUnassigned_EqualsTotalFileRecords()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        SeedFiles(context);

        var service = new FileService(context, new StubStorageService(), new CatalogTagPublisher(context));
        var pagination = new PaginationParams { Page = 1, PageSize = 100 };

        var assigned = await service.GetAssignedFilesAsync(pagination);
        var unassigned = await service.GetUnassignedFilesAsync(pagination);
        var totalFileRecords = await context.FileMetadata.CountAsync();

        Assert.Equal(3, assigned.TotalCount);
        Assert.Equal(2, unassigned.TotalCount);
        Assert.Equal(5, totalFileRecords);
        Assert.Equal(totalFileRecords, assigned.TotalCount + unassigned.TotalCount);
        Assert.All(assigned.Items, f => Assert.NotEqual(nameof(FileTaggingStatus.Unassigned), f.Status));
        Assert.All(unassigned.Items, f => Assert.Equal(nameof(FileTaggingStatus.Unassigned), f.Status));
    }

    [Fact]
    public async Task Unassigned_ListsFolderChildren_AndPagesOverThem()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.FileMetadata.AddRange(
            CreateFile("ProvA/Col1/a.wav", FileTaggingStatus.Unassigned),
            CreateFile("ProvA/Col1/b.wav", FileTaggingStatus.Unassigned),
            CreateFile("ProvA/Col2/c.wav", FileTaggingStatus.Unassigned),
            CreateFile("ProvB/Col1/d.wav", FileTaggingStatus.Unassigned),
            CreateFile("ProvB/Col1/e.wav", FileTaggingStatus.Assigned));
        context.SaveChanges();

        var service = new FileService(context, new StubStorageService(), new CatalogTagPublisher(context));

        var root = await service.GetUnassignedFilesAsync(new PaginationParams { Page = 1, PageSize = 1 });
        Assert.Equal(2, root.TotalCount);
        Assert.Equal(2, root.TotalPages);
        Assert.Equal(4, root.TotalFileCount);
        var first = Assert.Single(root.Items);
        Assert.True(first.IsDirectory);
        Assert.Equal("ProvA/", first.BlobName);
        Assert.Equal(3, first.FileCount);

        var col1 = await service.GetUnassignedFilesAsync(new PaginationParams { Page = 1, PageSize = 25, SortBy = "fileName" }, "ProvA/Col1");
        Assert.Equal("ProvA/Col1/", col1.Folder);
        Assert.Equal(["ProvA/Col1/a.wav", "ProvA/Col1/b.wav"], col1.Items.Select(f => f.BlobName));
        Assert.All(col1.Items, f => Assert.False(f.IsDirectory));
    }

    private static void SeedFiles(ApplicationDbContext context)
    {
        context.FileMetadata.AddRange(
            CreateFile("unassigned-1.wav", FileTaggingStatus.Unassigned),
            CreateFile("unassigned-2.wav", FileTaggingStatus.Unassigned),
            CreateFile("assigned-1.wav", FileTaggingStatus.Assigned),
            CreateFile("submitted-1.wav", FileTaggingStatus.SubmittedToSupervisor),
            CreateFile("approved-1.wav", FileTaggingStatus.ApprovedBySupervisor));

        context.SaveChanges();
    }

    private static FileMetadata CreateFile(string fileName, FileTaggingStatus status) => new()
    {
        FileName = fileName,
        FileUrl = $"https://example.com/{fileName}",
        BlobName = fileName,
        FileSize = 1024,
        ContentType = "audio/wav",
        Status = status,
        UploadedAt = DateTime.UtcNow
    };

    private sealed class StubStorageService : IFileStorageService
    {
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StorageFileEntry>>([]);

        public Task<StorageFileEntry?> GetEntryAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<StorageFileEntry?>(null);

        public Task<string?> GetReadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
