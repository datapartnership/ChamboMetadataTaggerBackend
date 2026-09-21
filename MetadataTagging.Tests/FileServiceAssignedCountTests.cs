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

        var service = new FileService(context, new StubStorageService());
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

    private sealed class StubStorageService : IStorageService
    {
        public Task<IEnumerable<BlobFileDto>> ListBlobsAsync(string? prefix = null) =>
            Task.FromResult(Enumerable.Empty<BlobFileDto>());

        public Task<string> GetBlobUrlAsync(string blobName) =>
            Task.FromResult(string.Empty);

        public Task<string> GetBlobSasUrlAsync(string blobName, int expiryMinutes = 60) =>
            Task.FromResult(string.Empty);

        public Task<bool> BlobExistsAsync(string blobName) =>
            Task.FromResult(false);

        public Task<(Stream Content, string ContentType, long ContentLength)> DownloadBlobAsync(string blobName) =>
            throw new NotSupportedException();

        public Task<(Stream Content, string ContentType, long ContentLength, long TotalSize)> DownloadBlobRangeAsync(
            string blobName, long offset, long? length) =>
            throw new NotSupportedException();

        public Task<long> GetBlobSizeAsync(string blobName) =>
            Task.FromResult(0L);
    }
}
