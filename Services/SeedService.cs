using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.DTOs;

namespace MetadataTagging.Services;

/// <summary>
/// Scans the configured file storage (local disk or S3) and populates the database
/// with file metadata, reading keywords and themes from Chambo's own FileMetadata/FileTags
/// tables (same ApplicationDbContext/database) when available and falling back to random
/// UNBIS taxonomy values otherwise. Mirrors the logic of seed_metadata.py.
/// </summary>
public class SeedService(ApplicationDbContext db, IFileStorageService storage)
{
    // ── UNBIS taxonomy ────────────────────────────────────────────────────────

    private static readonly string[] Themes =
    [
        "Agriculture Forestry and Fishing",
        "Culture",
        "Economic Development and Development Finance",
        "Education",
        "Employment",
        "Geographical Descriptors",
        "Health",
        "Human Settlements",
        "Humanitarian Aid and Relief",
        "Industry",
        "International Trade",
        "Natural Resources and the Environment",
        "Organizational Questions",
        "Political and Legal Questions",
        "Population",
        "Science and Technology",
        "Social Conditions and Equity",
        "Transport and Communications",
    ];

    private static readonly Dictionary<string, string[]> KeywordPools = new()
    {
        ["Agriculture Forestry and Fishing"] =
        [
            "maize", "tobacco", "farming", "drought", "harvest", "irrigation",
            "smallholder", "fertilizer", "crop", "fisheries", "deforestation",
            "livestock", "agribusiness", "food security", "seeds",
        ],
        ["Culture"] =
        [
            "tradition", "festival", "arts", "heritage", "music", "dance",
            "literature", "identity", "ceremony", "language", "folklore",
            "religion", "customs", "community", "celebration",
        ],
        ["Economic Development and Development Finance"] =
        [
            "GDP", "investment", "donor", "budget", "IMF", "World Bank",
            "inflation", "kwacha", "poverty", "microfinance", "grants",
            "development", "economic growth", "remittances", "taxation",
        ],
        ["Education"] =
        [
            "school", "university", "MSCE", "literacy", "teachers", "curriculum",
            "enrolment", "MANEB", "scholarships", "dropout", "STEM",
            "primary education", "secondary education", "TEVET", "bursary",
        ],
        ["Employment"] =
        [
            "jobs", "unemployment", "wages", "labour", "workers", "strike",
            "trade union", "minimum wage", "retrenchment", "hiring",
            "informal sector", "skills", "youth employment", "salaries", "benefits",
        ],
        ["Geographical Descriptors"] =
        [
            "Lilongwe", "Blantyre", "Mzuzu", "Zomba", "Northern Region",
            "Southern Region", "Central Region", "Lake Malawi", "Shire River",
            "Dedza", "Mangochi", "Karonga", "Nkhata Bay", "Kasungu", "Mulanje",
        ],
        ["Health"] =
        [
            "malaria", "HIV", "cholera", "clinic", "hospital", "vaccination",
            "maternal health", "nutrition", "mental health", "nurses", "doctors",
            "CHAM", "disease outbreak", "health budget", "medicines",
        ],
        ["Human Settlements"] =
        [
            "housing", "urban planning", "slums", "water supply", "sanitation",
            "land rights", "infrastructure", "township", "settlement",
            "displacement", "construction", "property", "zoning", "municipality", "city council",
        ],
        ["Humanitarian Aid and Relief"] =
        [
            "flood relief", "NGO", "aid", "refugees", "displacement",
            "emergency", "Red Cross", "UNHCR", "food aid", "disaster",
            "cyclone", "drought relief", "camps", "humanitarian", "donations",
        ],
        ["Industry"] =
        [
            "manufacturing", "factory", "textile", "cement", "sugar",
            "brewery", "export", "production", "MIPA", "SME",
            "industrial zone", "workers", "enterprise", "supply chain", "raw materials",
        ],
        ["International Trade"] =
        [
            "exports", "imports", "COMESA", "SADC", "tariffs", "trade deficit",
            "forex", "commodities", "trade agreement", "customs", "WTO",
            "balance of payments", "smuggling", "border", "trade route",
        ],
        ["Natural Resources and the Environment"] =
        [
            "climate change", "deforestation", "wildlife", "national park",
            "pollution", "mining", "oil", "gas", "water resources",
            "biodiversity", "conservation", "carbon", "emissions", "ecosystem", "environment",
        ],
        ["Organizational Questions"] =
        [
            "government", "parliament", "cabinet", "ministry", "parastatal",
            "reform", "civil service", "decentralization", "audit", "accountability",
            "transparency", "governance", "institution", "policy", "regulation",
        ],
        ["Political and Legal Questions"] =
        [
            "election", "MCP", "DPP", "UTM", "court", "constitution",
            "human rights", "corruption", "ACB", "judiciary", "legislation",
            "democracy", "opposition", "political party", "rule of law",
        ],
        ["Population"] =
        [
            "census", "birth rate", "migration", "urbanisation", "demographics",
            "family planning", "population growth", "youth", "elderly",
            "gender", "women", "children", "statistics", "NSO", "household",
        ],
        ["Science and Technology"] =
        [
            "internet", "mobile money", "innovation", "startup", "AI",
            "solar energy", "4G", "fintech", "e-government", "data",
            "research", "ICT", "digital", "biotechnology", "engineering",
        ],
        ["Social Conditions and Equity"] =
        [
            "poverty", "inequality", "gender-based violence", "marriage",
            "divorce", "children", "disability", "social protection",
            "MASAF", "welfare", "orphans", "community", "family", "rights", "inclusion",
        ],
        ["Transport and Communications"] =
        [
            "roads", "M1 highway", "bridge", "NICO", "Malawi Railways",
            "bus", "minibus", "airport", "Kamuzu International", "media",
            "radio", "television", "newspaper", "MBC", "telecommunications",
        ],
    };

    private static readonly string[] SampleAuthors =
    [
        "Martin Gela", "Grace Kamange", "Kondwani Munthali", "Tadala Mwale",
        "Chikondi Phiri", "Yankho Banda", "Mphatso Chirwa", "Tiyamike Nkhoma",
        "Limbani Tembo", "Fatsani Gondwe", "Dalitso Mkandawire", "Pemphero Msiska",
        "Lusungu Nkosi", "Takondwa Kayira", "Chisomo Kalimba",
    ];

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".txt", ".md" };

    // ── Public entry point ───────────────────────────────────────────────────

    public Task<SeedResult> SeedAsync(SeedRequest request, CancellationToken ct = default) =>
        request.Mode == SeedMode.Dummy
            ? SeedDummyAsync(request, ct)
            : SeedFromStorageAsync(ct);

    // ── Scan storage ─────────────────────────────────────────────────────────

    private async Task<SeedResult> SeedFromStorageAsync(CancellationToken ct)
    {
        var allFiles = await storage.ListFilesAsync("", ct);

        // Pre-load tagger tags keyed by normalised BlobName so we avoid N+1 queries
        var contentUrls = allFiles
            .Select(f => f.Key.StartsWith("./") ? f.Key : $"./{f.Key}")
            .ToList();
        var (taggerLookup, taggerError) = await LoadTaggerLookupAsync(contentUrls, ct);

        // Keep only supported file types, group by top-level directory = media house
        var filesByHouse = allFiles
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f.Key)))
            .GroupBy(f =>
            {
                var slash = f.Key.IndexOf('/');
                return slash > 0 ? f.Key[..slash] : string.Empty;
            })
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        int datasetsCreated = 0, filesInserted = 0, filesSkipped = 0, taggerFilesMatched = 0;

        foreach (var (houseName, files) in filesByHouse)
        {
            if (await EnsureDatasetAsync(houseName, ct)) datasetsCreated++;

            foreach (var entry in files)
            {
                var contentUrl = entry.Key.StartsWith("./") ? entry.Key : $"./{entry.Key}";

                if (await db.FileRecords.AnyAsync(f => f.ContentUrl == contentUrl, ct))
                {
                    filesSkipped++;
                    continue;
                }

                // For local files compute a real SHA-256; for S3 use the ETag to avoid
                // downloading potentially hundreds of megabytes per file.
                var sha256 = entry.ETag is { Length: > 0 } etag
                    ? etag
                    : await ComputeSha256Async(entry.Key, ct);

                var (themes, keywords, author, title) = ExtractTaggerData(contentUrl, taggerLookup);
                if (themes.Length > 0 || keywords.Length > 0 || author is not null || title is not null)
                    taggerFilesMatched++;

                var ext = Path.GetExtension(entry.Key).ToLowerInvariant();

                // PDFs are binary-compressed (~6 bytes per character); plain-text
                // formats (.txt, .md) have a 1:1 size-to-character ratio.
                var charEst  = ext == ".pdf" ? (int)(entry.Size / 6) : (int)entry.Size;
                var wordEst  = charEst / 5;
                var tokenEst = charEst / 4;

                var encodingFormat = ext switch
                {
                    ".txt" => "text/plain",
                    ".md"  => "text/markdown",
                    _      => "application/pdf",
                };

                db.FileRecords.Add(new FileRecord
                {
                    Id             = $"file-{houseName}-{Guid.NewGuid().ToString("N")[..8]}",
                    DatasetId      = DatasetId(houseName),
                    Name           = title ?? Path.GetFileName(entry.Key),
                    ContentUrl     = contentUrl,
                    ContentSize    = entry.Size,
                    Sha256         = sha256,
                    Author         = author,
                    DatePublished  = entry.LastModified.ToString("O"),
                    DateModified   = entry.LastModified.ToString("O"),
                    EncodingFormat = encodingFormat,
                    WordCount      = wordEst,
                    CharCount      = charEst,
                    TokenCount     = tokenEst,
                    NumberOfPages  = 1,
                    Keywords       = [.. keywords.Select(kw => new Keyword { Value = kw })],
                    Themes         = [.. themes.Select(th => new Theme   { Value = th })],
                });
                filesInserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new SeedResult
        {
            DatasetsCreated     = datasetsCreated,
            FilesInserted       = filesInserted,
            FilesSkipped        = filesSkipped,
            TaggerFilesMatched  = taggerFilesMatched,
            TaggerError         = taggerError,
            Message             = $"Scan complete: {datasetsCreated} dataset(s) created, " +
                                  $"{filesInserted} file(s) inserted, {filesSkipped} skipped, " +
                                  $"{taggerFilesMatched} enriched from tagger.",
        };
    }

    // ── Dummy data ────────────────────────────────────────────────────────────

    private async Task<SeedResult> SeedDummyAsync(SeedRequest request, CancellationToken ct)
    {
        var houses = string.IsNullOrWhiteSpace(request.DummyHouses)
            ? ["MediaHouseA", "MediaHouseB", "MediaHouseC", "MediaHouseD"]
            : request.DummyHouses
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int datasetsCreated = 0, filesInserted = 0;

        foreach (var houseName in houses)
        {
            if (await EnsureDatasetAsync(houseName, ct)) datasetsCreated++;

            for (int i = 0; i < request.DummyFilesPerHouse; i++)
            {
                var date      = RandomDateInYear(Random.Shared.Next(2022, 2026));
                var fakeName  = $"Article_{date:yyyyMMdd}_p{i + 1:D3}.pdf";
                var fakePath  = $"./{houseName}/{fakeName}";
                var charCount = Random.Shared.Next(800, 5001);
                var themes    = PickRandomThemes();
                var keywords  = PickRandomKeywords(themes);

                db.FileRecords.Add(new FileRecord
                {
                    Id            = $"file-{houseName}-{Guid.NewGuid().ToString("N")[..8]}",
                    DatasetId     = DatasetId(houseName),
                    Name          = fakeName,
                    ContentUrl    = fakePath,
                    ContentSize   = Random.Shared.Next(300_000, 5_000_001),
                    Sha256        = DeterministicHash(fakePath),
                    Author        = RandomAuthor(),
                    DatePublished = date.ToString("O"),
                    DateModified  = date.ToString("O"),
                    WordCount     = charCount / 5,
                    CharCount     = charCount,
                    TokenCount    = charCount / 4,
                    NumberOfPages = Random.Shared.Next(1, 5),
                    Keywords      = [.. keywords.Select(kw => new Keyword { Value = kw })],
                    Themes        = [.. themes.Select(th => new Theme   { Value = th })],
                });
                filesInserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new SeedResult
        {
            DatasetsCreated = datasetsCreated,
            FilesInserted   = filesInserted,
            FilesSkipped    = 0,
            Message         = $"Dummy records generated: {datasetsCreated} dataset(s) created, " +
                              $"{filesInserted} file(s) inserted.",
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DatasetId(string houseName) =>
        $"DDP-{houseName.ToUpperInvariant().Replace(' ', '-')}";

    /// <returns><c>true</c> when a new Dataset row was added, <c>false</c> when it already existed.</returns>
    private async Task<bool> EnsureDatasetAsync(string houseName, CancellationToken ct)
    {
        var id = DatasetId(houseName);
        if (await db.Datasets.FindAsync([id], ct) is not null)
            return false;

        db.Datasets.Add(new Dataset
        {
            Id            = id,
            Name          = houseName,
            DatePublished = DateTime.UtcNow.ToString("O"),
            DateModified  = DateTime.UtcNow.ToString("O"),
        });
        return true;
    }

    private async Task<string> ComputeSha256Async(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(key, ct);
            var hash = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            // Fall back to deterministic hash if the file cannot be opened
            return DeterministicHash(key);
        }
    }

    private static string DeterministicHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Tagger integration ─────────────────────────────────────────────────────

    // Only use tags that have been submitted to or approved by a supervisor
    private static readonly FileTaggingStatus[] ReadyStatuses =
        [FileTaggingStatus.SubmittedToSupervisor, FileTaggingStatus.ApprovedBySupervisor];

    private async Task<(Dictionary<string, FileMetadata> Lookup, string? Error)> LoadTaggerLookupAsync(
        List<string> contentUrls, CancellationToken ct)
    {
        if (contentUrls.Count == 0) return ([], null);

        // The tagger may store BlobNames with or without a leading "./".
        // Query for both forms so either convention matches.
        var stripped = contentUrls
            .Select(u => u.StartsWith("./") ? u[2..] : u)
            .ToList();

        try
        {
            var metadata = await db.FileMetadata
                .Include(m => m.Tags)
                .Where(m => m.BlobName != null
                            && (contentUrls.Contains(m.BlobName) || stripped.Contains(m.BlobName))
                            && ReadyStatuses.Contains(m.Status))
                .ToListAsync(ct);

            // Normalise keys to "./"-prefixed form to match contentUrl in the seed loop
            var lookup = metadata
                .Where(m => m.BlobName != null)
                .ToDictionary(
                    m => m.BlobName!.StartsWith("./") ? m.BlobName! : $"./{ m.BlobName!}",
                    m => m);

            return (lookup, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    /// <summary>
    /// Extracts themes, keywords, author, and title from the tagger database entry
    /// for the given file. Returns empty/null values when no approved tagger entry exists.
    /// </summary>
    private static (string[] themes, string[] keywords, string? author, string? title) ExtractTaggerData(
        string contentUrl,
        Dictionary<string, FileMetadata> taggerLookup)
    {
        if (!taggerLookup.TryGetValue(contentUrl, out var meta))
            return ([], [], null, null);

        string[] themes   = [];
        string[] keywords = [];
        string?  author   = null;
        string?  title    = null;

        foreach (var tag in meta.Tags)
        {
            switch (tag.TagKey)
            {
                case "Theme":
                    themes = tag.TagValue
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(t => t.Contains('>') ? t[..t.IndexOf('>')].Trim() : t.Trim())
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct()
                        .ToArray();
                    break;
                case "Keywords":
                    keywords = tag.TagValue
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToArray();
                    break;
                case "Author":
                    author = tag.TagValue;
                    break;
                case "Title":
                    title = tag.TagValue;
                    break;
            }
        }

        return (themes, keywords, author, title);
    }

    private static string[] PickRandomThemes()
    {
        var k = Random.Shared.Next(1, 4); // 1–3 themes
        return [.. Themes.OrderBy(_ => Random.Shared.Next()).Take(k)];
    }

    private static string[] PickRandomKeywords(string[] selectedThemes)
    {
        var pool = selectedThemes
            .SelectMany(t => KeywordPools.GetValueOrDefault(t, []))
            .Distinct()
            .ToArray();

        var k = Math.Max(3, Math.Min(6, pool.Length));
        return [.. pool.OrderBy(_ => Random.Shared.Next()).Take(k)];
    }

    private static string RandomAuthor() =>
        SampleAuthors[Random.Shared.Next(SampleAuthors.Length)];

    private static DateTime RandomDateInYear(int year)
    {
        var start = new DateTime(year, 1, 1);
        var span  = new DateTime(year, 12, 31) - start;
        return start.AddDays(Random.Shared.Next(0, span.Days + 1));
    }
}
