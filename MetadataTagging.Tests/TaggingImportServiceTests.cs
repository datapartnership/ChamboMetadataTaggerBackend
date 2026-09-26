using System.Text;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetadataTagging.Tests;

public class TaggingImportServiceTests
{
    private const string Dump = """
        --
        -- PostgreSQL database dump
        --

        \restrict TCSWiTwmUcJPE2fERGvH4IehuDzmHBEeAFTA0Cd2xyyXsv8PZCXzIDRDMrXMPbA

        SET statement_timeout = 0;

        COPY public."FileAssignments" ("Id", "FileMetadataId", "UserId", "AssignedAt", "AssignedByUserId", "CompletedAt", "IsCompleted", "IsCheckedBySupervisor", "CheckedBySupervisorId", "CheckedAt", "SupervisorNotes") FROM stdin;
        114	173	25	2026-04-29 07:48:26.900473+00	4	2026-04-30 07:22:38.174664+00	t	t	22	2026-05-02 12:49:01.19754+00	
        91	150	23	2026-04-29 07:36:37.707233+00	4	2026-04-30 07:43:15.200809+00	t	t	24	2026-08-19 10:02:27.371965+00	\N
        92	150	26	2026-04-20 07:36:37+00	4	\N	f	f	\N	\N	\N
        93	999	26	2026-04-20 07:36:37+00	4	\N	f	f	\N	\N	\N
        \.


        COPY public."FileMetadata" ("Id", "FileName", "FileUrl", "BlobName", "FileSize", "ContentType", "UploadedAt", "Status", "TaggingCompletedAt", "DurationSeconds") FROM stdin;
        150	a.wav	https://blob/a.wav	a.wav	1024	audio/wav	2026-04-01 10:00:00.5+00	3	2026-05-02 12:49:01+00	12.5
        173	b.wav	https://blob/b.wav	\N	2048	\N	2026-04-01 10:00:00+00	3	\N	\N
        \.


        COPY public."FileTags" ("Id", "FileMetadataId", "TagKey", "TagValue", "CreatedByUserId", "CreatedAt") FROM stdin;
        501	150	language	Chichewa	23	2026-04-30 07:40:00+00
        502	150	notes	line one\nline two\ttabbed \\ backslash, "quoted"	26	2026-04-30 07:40:00+00
        503	173	speaker	Ṱhúmbúka 😀	25	2026-04-30 07:20:00+00
        \.


        COPY public."Users" ("Id", "Username", "Email", "PasswordHash", "Role", "CreatedAt", "LastLoginAt", "IsActive") FROM stdin;
        23	old	old@local	hash	Tagger	2026-01-01 00:00:00+00	\N	t
        \.
        """;

    [Fact]
    public async Task Import_ReattributesToTheTwoUsers_AndKeepsOneAssignmentPerFile()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Users.Add(new User
        {
            Username = "Mert", Email = "MOzdag@worldbankgroup.org", Role = UserRoles.Admin,
            AuthProvider = UserAuthProviders.Entra, IsActive = true,
        });
        context.FileMetadata.Add(new FileMetadata { Id = 7, FileName = "stale.wav", FileUrl = "u" });
        await context.SaveChangesAsync();

        var service = new TaggingImportService(context, new CatalogTagPublisher(context), NullLogger<TaggingImportService>.Instance);
        var result = await service.ImportAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(Dump)), "mert.ozdag@gmail.com", "mozdag@worldbankgroup.org");

        var approver = await context.Users.SingleAsync(u => u.Email == "MOzdag@worldbankgroup.org");
        var tagger = await context.Users.SingleAsync(u => u.Email == "mert.ozdag@gmail.com");
        Assert.Equal(2, await context.Users.CountAsync());
        Assert.Equal(UserRoles.Tagger, tagger.Role);
        Assert.Equal(UserAuthProviders.Entra, tagger.AuthProvider);
        Assert.Null(tagger.PasswordHash);
        Assert.Equal(approver.Id, result.ApproverUserId);
        Assert.Equal(tagger.Id, result.TaggerUserId);

        Assert.Equal(new[] { 150, 173 }, await context.FileMetadata.OrderBy(f => f.Id).Select(f => f.Id).ToListAsync());
        var file150 = await context.FileMetadata.SingleAsync(f => f.Id == 150);
        Assert.Equal(FileTaggingStatus.ApprovedBySupervisor, file150.Status);
        Assert.Equal(new DateTime(2026, 4, 1, 10, 0, 0, 500, DateTimeKind.Utc), file150.UploadedAt);
        Assert.Equal(12.5, file150.DurationSeconds);

        var assignments = await context.FileAssignments.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(new[] { 91, 114 }, assignments.Select(a => a.Id));
        Assert.All(assignments, a =>
        {
            Assert.Equal(tagger.Id, a.UserId);
            Assert.Equal(approver.Id, a.AssignedByUserId);
            Assert.Equal(approver.Id, a.CheckedBySupervisorId);
            Assert.True(a.IsCheckedBySupervisor);
        });
        Assert.Null(assignments[0].SupervisorNotes);
        Assert.Equal("", assignments[1].SupervisorNotes);
        Assert.Equal(new DateTime(2026, 5, 2, 12, 49, 1, 197, DateTimeKind.Utc).AddTicks(5400), assignments[1].CheckedAt);

        var tags = await context.FileTags.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(3, tags.Count);
        Assert.All(tags, t => Assert.Equal(tagger.Id, t.CreatedByUserId));
        Assert.Equal("line one\nline two\ttabbed \\ backslash, \"quoted\"", tags[1].TagValue);
        Assert.Equal("Ṱhúmbúka 😀", tags[2].TagValue);

        Assert.Equal(4, result.AssignmentsInDump);
        Assert.Equal(2, result.AssignmentsImported);
        Assert.Equal(3, result.TagsImported);
    }

    [Fact]
    public async Task Import_NormalizesBlobNamesAndPublishesTagsToTheCatalog()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        const string key = "Radio/KBJ/a.wav";
        context.Collections.Add(new Collection
        {
            Id = "kbj", Name = "KBJ", Provider = "Radio", MediaType = MediaType.Audio, StoragePrefix = "Radio/KBJ",
            Files = [new CollectionFile { Id = "file-a", Name = "a.wav", ContentUrl = key, Kind = FileKind.Audio, EncodingFormat = "audio/wav" }],
        });
        await context.SaveChangesAsync();

        var dump = Dump
            .Replace("\ta.wav\t1024", $"\t./{key}\t1024")
            .Replace("\tlanguage\tChichewa\t", "\tKeywords\tradio, health\t");
        var service = new TaggingImportService(context, new CatalogTagPublisher(context), NullLogger<TaggingImportService>.Instance);
        await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(dump)), "t@x.org", "a@x.org");

        context.ChangeTracker.Clear();
        Assert.Equal(key, (await context.FileMetadata.SingleAsync(f => f.Id == 150)).BlobName);
        var file = await context.CollectionFiles.Include(f => f.Keywords).SingleAsync();
        Assert.Equal(["health", "radio"], file.Keywords.Select(k => k.Value).Order());
    }

    [Fact]
    public async Task Import_RejectsCustomFormatDump()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var service = new TaggingImportService(context, new CatalogTagPublisher(context), NullLogger<TaggingImportService>.Instance);
        var ex = await Assert.ThrowsAsync<FormatException>(() => service.ImportAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("PGDMP\u0001\u000e")), "a@x.com", "b@x.com"));
        Assert.Contains("plain SQL", ex.Message);
    }
}
