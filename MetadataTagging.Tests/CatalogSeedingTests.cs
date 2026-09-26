using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MetadataTagging.Data;
using MetadataTagging.DTOs;
using MetadataTagging.Models;
using MetadataTagging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetadataTagging.Tests;

public class CatalogSeedingTests
{
    private const string KbjPrefix   = "Radio/KBJ";
    private const string KbjMetadata = $"{KbjPrefix}/kbj_dataset_metadata.jsonld";

    private sealed record Entry(string Name, string? Format = null, double? Duration = null, string? ContentUrl = null);

    private static string Metadata(string? id, string name, params Entry[] files)
    {
        var doc = new Dictionary<string, object?>
        {
            ["sc:name"]      = name,
            ["distribution"] = files.Select(f => new Dictionary<string, object?>
            {
                ["sc:name"]           = f.Name,
                ["sc:contentUrl"]     = f.ContentUrl ?? $"/old/location/{f.Name}",
                ["sc:encodingFormat"] = f.Format,
                ["ebucore:duration"]  = f.Duration,
            }).ToList(),
        };
        if (id != null)
            doc["sc:identifier"] = id;
        return JsonSerializer.Serialize(doc);
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_PairsMediaWithTranscriptionByStem()
    {
        var json = Metadata("kbj", "KBJ",
            new("ep01.flac", "audio/flac"),
            new("ep01.txt"),
            new("ep02.flac", "audio/flac"),
            new("ep02.flac.txt", "text/plain"),
            new("ep03.flac", "audio/flac"));

        var (_, files) = new CroissantMetadataParser().Parse(json, KbjPrefix, "Radio", MediaType.Audio);
        var byName = files.ToDictionary(f => f.Name);

        Assert.Equal(FileKind.Transcription, byName["ep01.txt"].Kind);
        Assert.Equal("text/plain", byName["ep01.txt"].EncodingFormat);
        Assert.Equal(byName["ep01.txt"].Id, byName["ep01.flac"].TranscriptionFileId);
        Assert.Equal(byName["ep02.flac.txt"].Id, byName["ep02.flac"].TranscriptionFileId);
        Assert.Null(byName["ep03.flac"].TranscriptionFileId);
        Assert.Equal(FileKind.Audio, byName["ep01.flac"].Kind);
    }

    [Fact]
    public void Parser_PrefersSameFolderAndSkipsAmbiguousMatches()
    {
        var json = Metadata("kbj", "KBJ",
            new("a/ep01.flac", "audio/flac"),
            new("a/ep01.txt", "text/plain"),
            new("b/ep01.txt", "text/plain"),
            new("c/ep02.flac", "audio/flac"),
            new("a/ep02.txt", "text/plain"),
            new("b/ep02.txt", "text/plain"));

        var (_, files) = new CroissantMetadataParser().Parse(json, KbjPrefix, "Radio", MediaType.Audio);
        var byUrl = files.ToDictionary(f => f.ContentUrl);

        Assert.Equal(byUrl[$"{KbjPrefix}/a/ep01.txt"].Id, byUrl[$"{KbjPrefix}/a/ep01.flac"].TranscriptionFileId);
        Assert.Null(byUrl[$"{KbjPrefix}/c/ep02.flac"].TranscriptionFileId);
    }

    [Fact]
    public void Parser_KeepsContentUrlInsidePrefixAndRebuildsOthers()
    {
        var json = Metadata(null, "Clips",
            new("clip01.mp4", "video/mp4", ContentUrl: "/Radio/Clips/sub/clip01.mp4"),
            new("clip02.mp4", ContentUrl: "/somewhere/else/clip02.mp4"));

        var (collection, files) = new CroissantMetadataParser().Parse(json, "Radio/Clips", "Radio", MediaType.Video);

        Assert.Equal(CroissantMetadataParser.CollectionId("Radio/Clips"), collection.Id);
        Assert.Equal(["Radio/Clips/sub/clip01.mp4", "Radio/Clips/clip02.mp4"], files.Select(f => f.ContentUrl));
        Assert.All(files, f => Assert.Equal(FileKind.Video, f.Kind));
    }

    [Fact]
    public void Parser_ReadsLanguageThemesAndYears()
    {
        var json = """
        {
          "sc:identifier": "kbj",
          "sc:name": "KBJ",
          "olac:subjectLanguage": { "name": "Chichewa", "code": "nya" },
          "sc:keywords": ["health", "radio"],
          "dcat:theme": { "skos:prefLabel": "Health", "skos:hasTopConcept": [ { "skos:prefLabel": "Nutrition" } ] },
          "sc:temporalCoverage": "2013-01-01/2015-12-31",
          "distribution": [ "not-an-object" ]
        }
        """;

        var (collection, files) = new CroissantMetadataParser().Parse(json, KbjPrefix, "Radio", MediaType.Audio);

        Assert.Equal("Chichewa", collection.LanguageName);
        Assert.Equal("nya", collection.LanguageCode);
        Assert.Equal("health,radio", collection.Keywords);
        Assert.Equal("Health,Nutrition", collection.Themes);
        Assert.Equal(2013, collection.StartYear);
        Assert.Equal(2015, collection.EndYear);
        Assert.Empty(files);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seed_IgnoresFilesOutsideLayoutAndReportsWarnings()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            ["readme.txt"]                             = "x",
            ["Radio/notes.txt"]                        = "x",
            ["Radio/NoMetadata/ep01.flac"]             = "x",
            ["Radio/Photos/a.png"]                     = "x",
            ["Radio/.checkpoints/state.json"]          = "x",
            ["Radio/Hidden/.cache/h_metadata.jsonld"]  = "{}",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Empty(await ctx.Db.Collections.ToListAsync());
        Assert.Contains(result.Warnings, w =>
            w.Contains("2 file(s) are not inside a Provider/Collection folder") && w.Contains("readme.txt") && w.Contains("Radio/notes.txt"));
        Assert.Contains(result.Warnings, w => w.Contains("'Radio/NoMetadata' contains audio/video but no"));
        Assert.Contains(result.Warnings, w => w.Contains("'Radio/Photos' has no") && w.Contains(".pdf, .txt, or .md"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains(".checkpoints") || w.Contains("Hidden"));
    }

    [Fact]
    public async Task Seed_MetadataAtCollectionRootWithMediaInSubfolder()
    {
        await using var ctx = await CreateContextAsync();
        const string prefix = "National Statistics Office/NSO_verbatim_Round1";
        const string sub = $"{prefix}/1367_combined";
        var storage = new InMemoryStorage
        {
            [$"{prefix}/nso_round1_dataset_metadata.jsonld"] = Metadata("nso-r1", "NSO Round 1",
                new("a.flac", "audio/flac", ContentUrl: $"/{sub}/a.flac"),
                new("a.txt", "text/plain", ContentUrl: $"/{sub}/a.txt"),
                new("b.flac", "audio/flac", ContentUrl: $"/{sub}/b.flac")),
            [$"{sub}/a.flac"] = "A",
            [$"{sub}/a.txt"]  = "transcript a",
            [$"{sub}/b.flac"] = "B",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Empty(result.Warnings);
        var collection = await ctx.Db.Collections.Include(c => c.Files).SingleAsync();
        Assert.Equal(("nso-r1", "National Statistics Office", MediaType.Audio, prefix),
            (collection.Id, collection.Provider, collection.MediaType, collection.StoragePrefix));
        Assert.Equal(2, collection.FileCount);
        var a = collection.Files.Single(f => f.Name == "a.flac");
        Assert.Equal($"{sub}/a.flac", a.ContentUrl);
        Assert.Equal(collection.Files.Single(f => f.Name == "a.txt").Id, a.TranscriptionFileId);

        var bundle = await CreateBundleService(ctx.Db, storage).GetFileBundleAsync(a.Id);
        var entries = await ReadZipAsync(bundle!);
        Assert.Equal("transcript a", entries["a.txt"]);
    }

    [Fact]
    public async Task Seed_FindsFilesByNameWhenContentUrlIsStale()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new("ep01.flac", "audio/flac"), new("ep01.txt", "text/plain")),
            [$"{KbjPrefix}/audio/ep01.flac"]       = "FLAC",
            [$"{KbjPrefix}/transcripts/ep01.txt"]  = "transcript",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Contains(result.Warnings, w => w.Contains("2 file(s) listed in") && w.Contains("matched to a storage file by name"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("not found in storage"));
        var audio = await ctx.Db.CollectionFiles.SingleAsync(f => f.Name == "ep01.flac");
        var transcript = await ctx.Db.CollectionFiles.SingleAsync(f => f.Name == "ep01.txt");
        Assert.Equal($"{KbjPrefix}/audio/ep01.flac", audio.ContentUrl);
        Assert.Equal(CroissantMetadataParser.FileId(audio.ContentUrl), audio.Id);
        Assert.Equal(transcript.Id, audio.TranscriptionFileId);
    }

    [Fact]
    public async Task Seed_InfersMediaTypeFromContents()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new Entry("ep01.flac")),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC",
            ["Radio/Clips/clips_metadata.jsonld"] = Metadata("clips", "Clips", new("clip.mp4"), new("clip.bin")),
            ["Radio/Clips/clip.mp4"] = "MP4",
            ["Radio/Clips/clip.bin"] = "?",
            ["Ministry/Docs/a.pdf"] = "pdf",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        var types = await ctx.Db.Collections.ToDictionaryAsync(c => c.StoragePrefix, c => c.MediaType);
        Assert.Equal(MediaType.Audio, types[KbjPrefix]);
        Assert.Equal(MediaType.Video, types["Radio/Clips"]);
        Assert.Equal(MediaType.Text, types["Ministry/Docs"]);
        Assert.All(await ctx.Db.CollectionFiles.Where(f => f.CollectionId == "clips").ToListAsync(),
            f => Assert.Equal(FileKind.Video, f.Kind));
        Assert.Equal(1, result.ByMediaType["Video"].CollectionsAdded);
    }

    [Fact]
    public async Task Seed_TextCollection_ScansFilesAndAppliesTaggerTags()
    {
        await using var ctx = await CreateContextAsync();
        var tagged = new FileMetadata
        {
            FileName    = "doc1.txt",
            FileUrl     = "https://example.com/doc1.txt",
            BlobName    = "Ministry/Reports/doc1.txt",
            FileSize    = 10,
            ContentType = "text/plain",
            Status      = FileTaggingStatus.ApprovedBySupervisor,
            UploadedAt  = DateTime.UtcNow,
        };
        tagged.Tags.Add(new FileTag { TagKey = "Theme", TagValue = "Health > Clinics | Education" });
        tagged.Tags.Add(new FileTag { TagKey = "Keywords", TagValue = "malaria, schools" });
        tagged.Tags.Add(new FileTag { TagKey = "Author", TagValue = "Jane Banda" });
        tagged.Tags.Add(new FileTag { TagKey = "Title", TagValue = "Annual Report" });
        ctx.Db.FileMetadata.Add(tagged);
        await ctx.Db.SaveChangesAsync();

        var storage = new InMemoryStorage
        {
            ["Ministry/Reports/doc1.txt"] = "first paragraph",
            ["Ministry/Reports/sub/doc2.md"] = "# second",
            ["Ministry/Reports/ignored.docx"] = "binary",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Equal(1, result.ByMediaType["Text"].CollectionsAdded);
        Assert.Equal(2, result.ByMediaType["Text"].FilesAdded);
        Assert.Equal(1, result.TaggerFilesMatched);

        var collection = await ctx.Db.Collections.SingleAsync();
        Assert.Equal(CroissantMetadataParser.CollectionId("Ministry/Reports"), collection.Id);
        Assert.Equal(("Reports", "Ministry", MediaType.Text), (collection.Name, collection.Provider, collection.MediaType));
        Assert.Equal(2, collection.FileCount);
        Assert.Equal("text/markdown,text/plain", collection.Formats);
        Assert.Contains("Health", collection.Themes);
        Assert.Contains("malaria", collection.Keywords);

        var doc1 = await ctx.Db.CollectionFiles
            .Include(f => f.Keywords).Include(f => f.Themes)
            .SingleAsync(f => f.ContentUrl == "Ministry/Reports/doc1.txt");
        Assert.Equal("Annual Report", doc1.Name);
        Assert.Equal("Jane Banda", doc1.Author);
        Assert.Equal(FileKind.Text, doc1.Kind);
        Assert.Equal(["Education", "Health"], doc1.Themes.Select(t => t.Value).Order());
        Assert.Equal(["malaria", "schools"], doc1.Keywords.Select(k => k.Value).Order());
    }

    [Fact]
    public async Task Seed_AudioCollection_AppliesTaggerTagsToFilesAndCroissant()
    {
        await using var ctx = await CreateContextAsync();
        ctx.Db.FileMetadata.Add(TaggedFile($"{KbjPrefix}/ep01.flac", FileTaggingStatus.SubmittedToSupervisor,
            ("Theme", "Health > Nutrition"), ("Keywords", "maize, radio"), ("Author", "KBJ Radio"), ("Title", "Ignored for media")));
        await ctx.Db.SaveChangesAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new("ep01.flac", "audio/flac"), new("ep02.flac", "audio/flac")),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC1",
            [$"{KbjPrefix}/ep02.flac"] = "FLAC2",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Equal(1, result.TaggerFilesMatched);
        Assert.Equal(0, result.TaggedFilesNotCataloged);
        var ep01 = await ctx.Db.CollectionFiles.Include(f => f.Keywords).Include(f => f.Themes)
            .SingleAsync(f => f.Name == "ep01.flac");
        Assert.Equal("KBJ Radio", ep01.Author);
        Assert.Equal(["maize", "radio"], ep01.Keywords.Select(k => k.Value).Order());
        Assert.Equal(["Health"], ep01.Themes.Select(t => t.Value));
        Assert.Null((await ctx.Db.Collections.SingleAsync()).Keywords);

        var bundle = await CreateBundleService(ctx.Db, storage).GetCollectionBundleAsync("kbj", "https://api.test");
        using var metadata = JsonDocument.Parse((await ReadZipAsync(bundle!))["metadata.jsonld"]);
        var distribution = metadata.RootElement.GetProperty("@graph")[0].GetProperty("distribution").EnumerateArray()
            .Single(d => d.GetProperty("sc:name").GetString() == "ep01.flac");
        Assert.Equal("KBJ Radio", distribution.GetProperty("sc:author").GetString());
        Assert.Equal(2, distribution.GetProperty("sc:keywords").GetArrayLength());
        Assert.Equal("Health", distribution.GetProperty("dcat:theme")[0].GetString());
    }

    [Fact]
    public async Task Seed_ReportsReadyTaggedFilesThatAreNotCataloged()
    {
        await using var ctx = await CreateContextAsync();
        ctx.Db.FileMetadata.AddRange(
            TaggedFile("Ministry/Reports/report.docx", FileTaggingStatus.ApprovedBySupervisor, ("Keywords", "budget")),
            TaggedFile("Ministry/Reports/draft.docx", FileTaggingStatus.Assigned, ("Keywords", "draft")),
            TaggedFile("Ministry/Reports/doc.txt", FileTaggingStatus.ApprovedBySupervisor, ("Keywords", "health")));
        await ctx.Db.SaveChangesAsync();
        var storage = new InMemoryStorage
        {
            ["Ministry/Reports/doc.txt"] = "text",
            ["Ministry/Reports/report.docx"] = "binary",
            ["Ministry/Reports/draft.docx"] = "binary",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Equal(1, result.TaggerFilesMatched);
        Assert.Equal(1, result.TaggedFilesNotCataloged);
        Assert.Equal(["Ministry/Reports/report.docx"], result.TaggedFilesNotCatalogedExamples);
        Assert.Contains(result.Warnings, w => w.Contains("1 submitted or approved tagged file(s)") && w.Contains("report.docx"));
    }

    [Fact]
    public async Task Supervisor_EditSendBackAndApprove_UpdateCatalogWithoutReseed()
    {
        await using var ctx = await CreateContextAsync();
        var tagger = new User { Username = "tagger", Email = "t@x.org", Role = UserRoles.Tagger };
        var supervisor = new User { Username = "supervisor", Email = "s@x.org", Role = UserRoles.Supervisor };
        var file = TaggedFile("Ministry/Reports/doc1.txt", FileTaggingStatus.SubmittedToSupervisor,
            ("Keywords", "malaria"), ("Title", "Annual Report"), ("Author", "Jane Banda"));
        ctx.Db.AddRange(tagger, supervisor, file);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.AddRange(
            new StudentSupervisor { StudentId = tagger.Id, SupervisorId = supervisor.Id, AssignedByUserId = supervisor.Id },
            new FileAssignment { FileMetadataId = file.Id, UserId = tagger.Id, IsCompleted = true });
        await ctx.Db.SaveChangesAsync();

        var storage = new InMemoryStorage
        {
            ["Ministry/Reports/doc1.txt"] = "first",
            ["Ministry/Reports/doc2.txt"] = "second",
        };
        await CreateSeeder(ctx.Db, storage).SeedAsync();
        Assert.Equal("malaria", (await ReloadAsync(ctx.Db)).Collection.Keywords);

        var supervisors = new SupervisorService(ctx.Db, new CatalogTagPublisher(ctx.Db));

        Assert.True(await supervisors.EditFileTagsAsync(file.Id, tagger.Id, supervisor.Id,
            [new TagDto { TagKey = "Keywords", TagValue = "water, sanitation" }], null));
        var (collection, doc1) = await ReloadAsync(ctx.Db);
        Assert.Equal(["sanitation", "water"], doc1.Keywords.Select(k => k.Value).Order());
        Assert.Equal("sanitation,water", collection.Keywords);

        Assert.True(await supervisors.SendBackToTaggerAsync(file.Id, tagger.Id, supervisor.Id, "redo"));
        (collection, doc1) = await ReloadAsync(ctx.Db);
        Assert.Empty(doc1.Keywords);
        Assert.Equal(("doc1.txt", null), (doc1.Name, doc1.Author));
        Assert.Null(collection.Keywords);

        Assert.True(await supervisors.MarkFileAsCheckedAsync(file.Id, tagger.Id, supervisor.Id, null));
        (collection, doc1) = await ReloadAsync(ctx.Db);
        Assert.Equal(("Annual Report", "Jane Banda"), (doc1.Name, doc1.Author));
        Assert.Equal("sanitation,water", collection.Keywords);

        static async Task<(Collection Collection, CollectionFile Doc1)> ReloadAsync(ApplicationDbContext db)
        {
            db.ChangeTracker.Clear();
            var doc1 = await db.CollectionFiles.Include(f => f.Collection).Include(f => f.Keywords)
                .SingleAsync(f => f.ContentUrl == "Ministry/Reports/doc1.txt");
            return (doc1.Collection!, doc1);
        }
    }

    [Fact]
    public async Task Seed_AudioAndVideo_ReadJsonldAndLinkTranscriptions()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ",
                new("ep01.flac", "audio/flac", 60),
                new("ep01.txt"),
                new("ep02.flac", "audio/flac", 30),
                new("ep04.flac", "audio/flac", 10)),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC1",
            [$"{KbjPrefix}/ep01.txt"]  = "transcript",
            [$"{KbjPrefix}/ep02.flac"] = "FLAC2",
            [$"{KbjPrefix}/ep03.flac"] = "unlisted",
            ["Radio/Clips/clips_metadata.jsonld"] = Metadata(null, "Clips",
                new("clip.mp4", "video/mp4", 5),
                new("clip.txt", "text/plain")),
            ["Radio/Clips/clip.mp4"] = "MP4",
            ["Radio/Clips/clip.txt"] = "caption",
        };

        var result = await CreateSeeder(ctx.Db, storage).SeedAsync();

        Assert.Equal(1, result.ByMediaType["Audio"].CollectionsAdded);
        Assert.Equal(4, result.ByMediaType["Audio"].FilesAdded);
        Assert.Equal(1, result.ByMediaType["Video"].CollectionsAdded);
        Assert.Contains(result.Warnings, w => w.Contains("1 file(s) listed in") && w.Contains("kbj_dataset_metadata.jsonld"));
        Assert.Contains(result.Warnings, w => w.Contains($"1 media file(s) in '{KbjPrefix}' are not listed"));

        var kbj = await ctx.Db.Collections.Include(c => c.Files).SingleAsync(c => c.Id == "kbj");
        Assert.Equal(MediaType.Audio, kbj.MediaType);
        Assert.Equal(KbjPrefix, kbj.StoragePrefix);
        Assert.Equal(3, kbj.FileCount);
        Assert.Equal(100, kbj.TotalDurationSeconds);
        Assert.Equal("audio/flac", kbj.Formats);
        var ep01 = kbj.Files.Single(f => f.Name == "ep01.flac");
        Assert.Equal($"{KbjPrefix}/ep01.flac", ep01.ContentUrl);
        Assert.Equal(5, ep01.ContentSize);
        Assert.Equal(kbj.Files.Single(f => f.Name == "ep01.txt").Id, ep01.TranscriptionFileId);

        var clips = await ctx.Db.Collections.Include(c => c.Files).SingleAsync(c => c.MediaType == MediaType.Video);
        Assert.Equal(CroissantMetadataParser.CollectionId("Radio/Clips"), clips.Id);
        var clip = clips.Files.Single(f => f.Kind == FileKind.Video);
        Assert.Equal(clips.Files.Single(f => f.Kind == FileKind.Transcription).Id, clip.TranscriptionFileId);
    }

    [Fact]
    public async Task Seed_Resync_UpdatesChangedRowsAndDeletesMissingOnes()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new("ep01.flac", "audio/flac"), new("ep02.flac", "audio/flac")),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC1",
            [$"{KbjPrefix}/ep02.flac"] = "FLAC2",
            ["Ministry/Docs/a.txt"] = "a",
            ["Ministry/Docs/b.txt"] = "b",
            ["Ministry/Old/c.txt"]  = "c",
        };
        var seeder = CreateSeeder(ctx.Db, storage);
        var first = await seeder.SeedAsync();
        Assert.Equal(3, first.Totals.CollectionsAdded);
        Assert.Equal(5, first.Totals.FilesAdded);

        storage[KbjMetadata] = Metadata("kbj", "KBJ (revised)", new Entry("ep01.flac", "audio/flac"));
        storage.Remove($"{KbjPrefix}/ep02.flac");
        storage.Remove("Ministry/Docs/b.txt");
        storage.Remove("Ministry/Old/c.txt");

        var second = await seeder.SeedAsync();

        Assert.Equal(1, second.ByMediaType["Audio"].CollectionsUpdated);
        Assert.Equal(1, second.ByMediaType["Audio"].FilesDeleted);
        Assert.Equal(1, second.ByMediaType["Text"].CollectionsUpdated);
        Assert.Equal(1, second.ByMediaType["Text"].CollectionsDeleted);
        Assert.Equal(2, second.ByMediaType["Text"].FilesDeleted);
        Assert.Equal(0, second.Totals.CollectionsAdded + second.Totals.FilesAdded);

        ctx.Db.ChangeTracker.Clear();
        Assert.Equal("KBJ (revised)", (await ctx.Db.Collections.SingleAsync(c => c.Id == "kbj")).Name);
        Assert.Equal(2, await ctx.Db.Collections.CountAsync());
        Assert.Equal(["Ministry/Docs/a.txt", $"{KbjPrefix}/ep01.flac"],
            (await ctx.Db.CollectionFiles.Select(f => f.ContentUrl).ToListAsync()).Order(StringComparer.Ordinal));

        var third = await seeder.SeedAsync();
        var t = third.Totals;
        Assert.Equal(0, t.CollectionsAdded + t.CollectionsUpdated + t.CollectionsDeleted);
        Assert.Equal(0, t.FilesAdded + t.FilesUpdated + t.FilesDeleted);
    }

    // ── Query and Croissant output ────────────────────────────────────────────

    [Fact]
    public async Task Query_SamplesOnlyMediaAndReturnsTheirTranscriptions()
    {
        await using var ctx = await CreateContextAsync();
        var entries = Enumerable.Range(1, 10)
            .SelectMany(i => new Entry[] { new($"ep{i:00}.flac", "audio/flac"), new($"ep{i:00}.txt", "text/plain") })
            .ToArray();
        var storage = new InMemoryStorage { [KbjMetadata] = Metadata("kbj", "KBJ", entries) };
        await CreateSeeder(ctx.Db, storage).SeedAsync();

        var result = await new CollectionQueryService(ctx.Db).QueryAsync(new CollectionQueryParams { Limit = 4 });

        var sampled = result.Groups.SelectMany(g => g.Files).ToList();
        Assert.Equal(10, result.TotalMatched);
        Assert.Equal(4, result.AppliedLimit);
        Assert.Equal(4, sampled.Count);
        Assert.All(sampled, f => Assert.Equal(FileKind.Audio, f.Kind));
        Assert.All(sampled, f => Assert.True(result.Transcriptions.ContainsKey(f.TranscriptionFileId!)));

        var json = new CroissantBuilder().Build(
            result.Groups, result.TotalMatched, result.AppliedLimit, "https://api.test", result.Transcriptions, result.TaggerTags);
        using var doc = JsonDocument.Parse(json);
        var dataset = doc.RootElement.GetProperty("@graph")[0];
        Assert.Equal("Audio", dataset.GetProperty("ddpv:mediaType").GetString());
        Assert.Equal("https://api.test/api/collections/kbj/bundle", dataset.GetProperty("ddpv:bundleUrl").GetString());
        var first = dataset.GetProperty("distribution")[0];
        Assert.StartsWith("https://api.test/api/files/", first.GetProperty("ddpv:bundleUrl").GetString());
        Assert.Equal("text/plain", first.GetProperty("ddpv:transcription").GetProperty("sc:encodingFormat").GetString());
    }

    [Fact]
    public async Task Query_FiltersByMediaType()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new Entry("ep01.flac", "audio/flac")),
            ["Ministry/Docs/a.txt"] = "a",
        };
        await CreateSeeder(ctx.Db, storage).SeedAsync();
        var service = new CollectionQueryService(ctx.Db);

        var text = await service.QueryAsync(new CollectionQueryParams { MediaType = "text" });
        var audio = await service.QueryAsync(new CollectionQueryParams { MediaType = "Audio" });
        var video = await service.QueryAsync(new CollectionQueryParams { MediaType = "Video" });

        Assert.Equal(FileKind.Text, text.Groups.Single().Files.Single().Kind);
        Assert.Equal("kbj", audio.Groups.Single().Collection.Id);
        Assert.Equal(0, video.TotalMatched);
    }

    // ── Bundles ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileBundle_ContainsMediaAndTranscription()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new("ep01.flac", "audio/flac"), new("ep01.txt", "text/plain")),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC",
            [$"{KbjPrefix}/ep01.txt"]  = "hello world",
        };
        await CreateSeeder(ctx.Db, storage).SeedAsync();
        var audio = await ctx.Db.CollectionFiles.SingleAsync(f => f.Name == "ep01.flac");
        var bundles = CreateBundleService(ctx.Db, storage);

        foreach (var id in new[] { audio.Id, audio.TranscriptionFileId! })
        {
            var bundle = await bundles.GetFileBundleAsync(id);

            Assert.NotNull(bundle);
            Assert.Equal("ep01.zip", bundle.FileName);
            var entries = await ReadZipAsync(bundle);
            Assert.Equal("FLAC", entries["ep01.flac"]);
            Assert.Equal("hello world", entries["ep01.txt"]);
        }
    }

    [Fact]
    public async Task FileBundle_ThrowsWhenLinkedTranscriptionIsMissingFromStorage()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ", new("ep01.flac", "audio/flac"), new("ep01.txt", "text/plain")),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC",
        };
        await CreateSeeder(ctx.Db, storage).SeedAsync();
        var audio = await ctx.Db.CollectionFiles.SingleAsync(f => f.Name == "ep01.flac");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => CreateBundleService(ctx.Db, storage).GetFileBundleAsync(audio.Id));
    }

    [Fact]
    public async Task Bundles_AreNotAvailableForText()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage { ["Ministry/Docs/a.txt"] = "a" };
        await CreateSeeder(ctx.Db, storage).SeedAsync();
        var file = await ctx.Db.CollectionFiles.SingleAsync();
        var bundles = CreateBundleService(ctx.Db, storage);

        Assert.Null(await bundles.GetFileBundleAsync(file.Id));
        Assert.Null(await bundles.GetCollectionBundleAsync(file.CollectionId, null));
    }

    [Fact]
    public async Task CollectionBundle_ContainsPairsMetadataAndMissingFileList()
    {
        await using var ctx = await CreateContextAsync();
        var storage = new InMemoryStorage
        {
            [KbjMetadata] = Metadata("kbj", "KBJ",
                new("ep01.flac", "audio/flac"), new("ep01.txt", "text/plain"), new("ep02.flac", "audio/flac")),
            [$"{KbjPrefix}/ep01.flac"] = "FLAC1",
            [$"{KbjPrefix}/ep01.txt"]  = "transcript 1",
        };
        await CreateSeeder(ctx.Db, storage).SeedAsync();

        var bundle = await CreateBundleService(ctx.Db, storage).GetCollectionBundleAsync("kbj", "https://api.test");

        Assert.NotNull(bundle);
        Assert.Equal("KBJ.zip", bundle.FileName);
        var entries = await ReadZipAsync(bundle);
        Assert.Equal("FLAC1", entries["ep01.flac"]);
        Assert.Equal("transcript 1", entries["ep01.txt"]);
        Assert.Contains($"{KbjPrefix}/ep02.flac", entries["MISSING_FILES.txt"]);
        Assert.Contains("ddpv:transcription", entries["metadata.jsonld"]);

        Assert.Null(await CreateBundleService(ctx.Db, storage).GetCollectionBundleAsync("nope", null));
    }

    [Fact]
    public async Task ZipStreamResult_WritesArchiveToNonSeekableResponseBody()
    {
        var body = new NonSeekableStream();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Response.Body = body;
        var bundle = new ZipBundle("ep01 é.zip", async (zip, ct) =>
        {
            await using var entry = zip.CreateEntry("ep01.txt").Open();
            await entry.WriteAsync(Encoding.UTF8.GetBytes("hello"), ct);
        });

        await new ZipStreamResult(bundle).ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext
        {
            HttpContext = http
        });

        Assert.Equal("application/zip", http.Response.ContentType);
        Assert.Contains("filename*=UTF-8''ep01%20%C3%A9.zip", http.Response.Headers.ContentDisposition.ToString());
        using var read = new ZipArchive(new MemoryStream(body.Inner.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(read.GetEntry("ep01.txt")!.Open());
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class NonSeekableStream : Stream
    {
        public MemoryStream Inner { get; } = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);
    }

    private static FileMetadata TaggedFile(string blobName, FileTaggingStatus status, params (string Key, string Value)[] tags)
    {
        var file = new FileMetadata
        {
            FileName    = Path.GetFileName(blobName),
            FileUrl     = blobName,
            BlobName    = blobName,
            Status      = status,
            UploadedAt  = DateTime.UtcNow,
        };
        foreach (var (key, value) in tags)
            file.Tags.Add(new FileTag { TagKey = key, TagValue = value });
        return file;
    }

    private static SeedService CreateSeeder(ApplicationDbContext db, IFileStorageService storage) =>
        new(db, storage, new CroissantMetadataParser(), new CatalogTagPublisher(db));

    private static CollectionBundleService CreateBundleService(ApplicationDbContext db, IFileStorageService storage) =>
        new(db, storage, new CroissantBuilder(), NullLogger<CollectionBundleService>.Instance);

    private static async Task<Dictionary<string, string>> ReadZipAsync(ZipBundle bundle)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            await bundle.WriteAsync(zip, CancellationToken.None);

        buffer.Position = 0;
        using var read = new ZipArchive(buffer, ZipArchiveMode.Read);
        var result = new Dictionary<string, string>();
        foreach (var entry in read.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            result[entry.FullName] = await reader.ReadToEndAsync();
        }
        return result;
    }

    private sealed class TestContext(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return new TestContext(connection, db);
    }

    /// <summary>Storage keyed by path; every entry reports the same timestamp so re-seeds are stable.</summary>
    private sealed class InMemoryStorage : Dictionary<string, string>, IFileStorageService
    {
        private static readonly DateTimeOffset Timestamp = new(2024, 5, 1, 0, 0, 0, TimeSpan.Zero);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
            TryGetValue(key, out var content)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)))
                : throw new FileNotFoundException(key);

        public async Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken ct = default)
        {
            await using var stream = await OpenReadAsync(key, ct);
            var bytes = ((MemoryStream)stream).ToArray().AsMemory((int)offset, (int)length);
            return new MemoryStream(bytes.ToArray());
        }

        public Task<IReadOnlyList<StorageFileEntry>> ListFilesAsync(string prefix = "", CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StorageFileEntry>>(
                Keys.Where(k => k.StartsWith(prefix))
                    .Select(Entry)
                    .ToList());

        public Task<StorageFileEntry?> GetEntryAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(ContainsKey(key) ? Entry(key) : null);

        public Task<string?> GetReadUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        private StorageFileEntry Entry(string key) => new(key, Encoding.UTF8.GetByteCount(this[key]), Timestamp);
    }
}
