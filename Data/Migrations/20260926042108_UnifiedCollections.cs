using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MetadataTagging.Data.Migrations
{
    /// <summary>
    /// Replaces the separate text (Datasets/FileRecords), sound (SoundDatasets/SoundFileRecords)
    /// and Excel-sourced (Collections) catalog tables with the unified Collections/CollectionFiles
    /// model. The catalog tables are derived data: they are dropped and recreated empty, and
    /// repopulated by POST /api/seed. Keywords/Themes are recreated so their foreign keys point
    /// at CollectionFiles.
    /// </summary>
    public partial class UnifiedCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Keywords");
            migrationBuilder.DropTable(name: "Themes");
            migrationBuilder.DropTable(name: "FileRecords");
            migrationBuilder.DropTable(name: "SoundFileRecords");
            migrationBuilder.DropTable(name: "Datasets");
            migrationBuilder.DropTable(name: "SoundDatasets");
            migrationBuilder.DropTable(name: "Collections");

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StoragePrefix = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LanguageName = table.Column<string>(type: "text", nullable: true),
                    LanguageCode = table.Column<string>(type: "text", nullable: true),
                    Keywords = table.Column<string>(type: "text", nullable: true),
                    Themes = table.Column<string>(type: "text", nullable: true),
                    TemporalCoverage = table.Column<string>(type: "text", nullable: true),
                    StartYear = table.Column<int>(type: "integer", nullable: true),
                    EndYear = table.Column<int>(type: "integer", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    License = table.Column<string>(type: "text", nullable: true),
                    Creator = table.Column<string>(type: "text", nullable: true),
                    Publisher = table.Column<string>(type: "text", nullable: true),
                    SourceOrganization = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "text", nullable: true),
                    PiiScreening = table.Column<bool>(type: "boolean", nullable: false),
                    SensitiveContent = table.Column<bool>(type: "boolean", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    TotalDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    Formats = table.Column<string>(type: "text", nullable: true),
                    SeededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CollectionId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ContentUrl = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EncodingFormat = table.Column<string>(type: "text", nullable: false),
                    ContentSize = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: true),
                    CharCount = table.Column<int>(type: "integer", nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: true),
                    NumberOfPages = table.Column<int>(type: "integer", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: true),
                    SampleRate = table.Column<double>(type: "double precision", nullable: true),
                    Channels = table.Column<int>(type: "integer", nullable: true),
                    Bitrate = table.Column<double>(type: "double precision", nullable: true),
                    IntegratedLoudness = table.Column<double>(type: "double precision", nullable: true),
                    SignalToNoiseRatio = table.Column<double>(type: "double precision", nullable: true),
                    TranscriptionFileId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionFiles_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            CreateTagTable(migrationBuilder, "Keywords", "CollectionFiles");
            CreateTagTable(migrationBuilder, "Themes", "CollectionFiles");

            migrationBuilder.CreateIndex(name: "idx_collections_mediatype", table: "Collections", column: "MediaType");
            migrationBuilder.CreateIndex(name: "idx_collections_provider", table: "Collections", column: "Provider");
            migrationBuilder.CreateIndex(name: "idx_collections_language", table: "Collections", column: "LanguageCode");

            migrationBuilder.CreateIndex(name: "idx_collectionfiles_author", table: "CollectionFiles", column: "Author");
            migrationBuilder.CreateIndex(name: "idx_collectionfiles_collection", table: "CollectionFiles", column: "CollectionId");
            migrationBuilder.CreateIndex(name: "idx_collectionfiles_contenturl", table: "CollectionFiles", column: "ContentUrl", unique: true);
            migrationBuilder.CreateIndex(name: "idx_collectionfiles_kind", table: "CollectionFiles", column: "Kind");

            CreateTagIndexes(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Keywords");
            migrationBuilder.DropTable(name: "Themes");
            migrationBuilder.DropTable(name: "CollectionFiles");
            migrationBuilder.DropTable(name: "Collections");

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Format = table.Column<string>(type: "text", nullable: true),
                    Hours = table.Column<double>(type: "double precision", nullable: true),
                    Items = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    StartYear = table.Column<int>(type: "integer", nullable: true),
                    EndYear = table.Column<int>(type: "integer", nullable: true),
                    Themes = table.Column<string>(type: "text", nullable: true),
                    Collected = table.Column<string>(type: "text", nullable: true),
                    Processing = table.Column<string>(type: "text", nullable: true),
                    Availability = table.Column<string>(type: "text", nullable: true),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    TaggedForText = table.Column<string>(type: "text", nullable: true),
                    StoragePathVerified = table.Column<bool>(type: "boolean", nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Datasets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Creator = table.Column<string>(type: "text", nullable: false),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    License = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    SpatialCoverage = table.Column<string>(type: "text", nullable: false),
                    TemporalCoverage = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Datasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SoundDatasets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreatorName = table.Column<string>(type: "text", nullable: true),
                    DateCreated = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    InLanguageIdentifier = table.Column<string>(type: "text", nullable: true),
                    InLanguageName = table.Column<string>(type: "text", nullable: true),
                    Keywords = table.Column<string>(type: "text", nullable: true),
                    License = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PiiScreening = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderName = table.Column<string>(type: "text", nullable: true),
                    SensitiveContent = table.Column<bool>(type: "boolean", nullable: false),
                    SourceOrganizationName = table.Column<string>(type: "text", nullable: true),
                    SpatialCoverage = table.Column<string>(type: "text", nullable: true),
                    SubjectLanguageCode = table.Column<string>(type: "text", nullable: true),
                    SubjectLanguageName = table.Column<string>(type: "text", nullable: true),
                    TemporalCoverage = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoundDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DatasetId = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: true),
                    CharCount = table.Column<int>(type: "integer", nullable: true),
                    ContentSize = table.Column<long>(type: "bigint", nullable: true),
                    ContentUrl = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    DiscourseType = table.Column<string>(type: "text", nullable: false),
                    EncodingFormat = table.Column<string>(type: "text", nullable: false),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NumberOfPages = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileRecords_Datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoundFileRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SoundDatasetId = table.Column<string>(type: "text", nullable: false),
                    Bitrate = table.Column<double>(type: "double precision", nullable: false),
                    Channels = table.Column<int>(type: "integer", nullable: false),
                    ContentSize = table.Column<long>(type: "bigint", nullable: true),
                    ContentUrl = table.Column<string>(type: "text", nullable: false),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: false),
                    EncodingFormat = table.Column<string>(type: "text", nullable: false),
                    IntegratedLoudness = table.Column<double>(type: "double precision", nullable: true),
                    IsTranscription = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SampleRate = table.Column<double>(type: "double precision", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    SignalToNoiseRatio = table.Column<double>(type: "double precision", nullable: true),
                    TranscriptionFileId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoundFileRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoundFileRecords_SoundDatasets_SoundDatasetId",
                        column: x => x.SoundDatasetId,
                        principalTable: "SoundDatasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            CreateTagTable(migrationBuilder, "Keywords", "FileRecords");
            CreateTagTable(migrationBuilder, "Themes", "FileRecords");

            migrationBuilder.CreateIndex(name: "idx_collections_availability", table: "Collections", column: "Availability");
            migrationBuilder.CreateIndex(name: "idx_collections_collected", table: "Collections", column: "Collected");
            migrationBuilder.CreateIndex(name: "idx_collections_language", table: "Collections", column: "Language");
            migrationBuilder.CreateIndex(name: "idx_collections_processing", table: "Collections", column: "Processing");
            migrationBuilder.CreateIndex(name: "idx_collections_type", table: "Collections", column: "Type");

            migrationBuilder.CreateIndex(name: "idx_filerecords_author", table: "FileRecords", column: "Author");
            migrationBuilder.CreateIndex(name: "idx_filerecords_dataset", table: "FileRecords", column: "DatasetId");
            migrationBuilder.CreateIndex(name: "idx_filerecords_date", table: "FileRecords", column: "DatePublished");

            migrationBuilder.CreateIndex(name: "idx_soundfilerecords_contenturl", table: "SoundFileRecords", column: "ContentUrl", unique: true);
            migrationBuilder.CreateIndex(name: "idx_soundfilerecords_dataset", table: "SoundFileRecords", column: "SoundDatasetId");
            migrationBuilder.CreateIndex(name: "idx_soundfilerecords_istranscription", table: "SoundFileRecords", column: "IsTranscription");

            CreateTagIndexes(migrationBuilder);
        }

        private static void CreateTagTable(MigrationBuilder migrationBuilder, string table, string fileTable)
        {
            migrationBuilder.CreateTable(
                name: table,
                columns: t => new
                {
                    Id = t.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = t.Column<string>(type: "text", nullable: false),
                    Value = t.Column<string>(type: "text", nullable: false)
                },
                constraints: t =>
                {
                    t.PrimaryKey($"PK_{table}", x => x.Id);
                    t.ForeignKey(
                        name: $"FK_{table}_{fileTable}_FileId",
                        column: x => x.FileId,
                        principalTable: fileTable,
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        private static void CreateTagIndexes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "idx_keywords_file", table: "Keywords", column: "FileId");
            migrationBuilder.CreateIndex(name: "idx_keywords_value", table: "Keywords", column: "Value");
            migrationBuilder.CreateIndex(name: "idx_themes_file", table: "Themes", column: "FileId");
            migrationBuilder.CreateIndex(name: "idx_themes_value", table: "Themes", column: "Value");
        }
    }
}
