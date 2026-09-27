using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MetadataTagging.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "DatasetAccessApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Organization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizationEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProjectDescription = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    EthicalDataHandling = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    OpenSourceCommitment = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetAccessApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Datasets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TemporalCoverage = table.Column<string>(type: "text", nullable: true),
                    SpatialCoverage = table.Column<string>(type: "text", nullable: false),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    Creator = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    License = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Datasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileDownloadLogs",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileDownloadLogs", x => new { x.UserId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "FileMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TaggingCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SoundDatasets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "text", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    DateCreated = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    TemporalCoverage = table.Column<string>(type: "text", nullable: true),
                    SpatialCoverage = table.Column<string>(type: "text", nullable: true),
                    License = table.Column<string>(type: "text", nullable: true),
                    SubjectLanguageName = table.Column<string>(type: "text", nullable: true),
                    SubjectLanguageCode = table.Column<string>(type: "text", nullable: true),
                    InLanguageName = table.Column<string>(type: "text", nullable: true),
                    InLanguageIdentifier = table.Column<string>(type: "text", nullable: true),
                    CreatorName = table.Column<string>(type: "text", nullable: true),
                    ProviderName = table.Column<string>(type: "text", nullable: true),
                    SourceOrganizationName = table.Column<string>(type: "text", nullable: true),
                    PiiScreening = table.Column<bool>(type: "boolean", nullable: false),
                    SensitiveContent = table.Column<bool>(type: "boolean", nullable: false),
                    Keywords = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoundDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AuthProvider = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DatasetId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ContentUrl = table.Column<string>(type: "text", nullable: true),
                    EncodingFormat = table.Column<string>(type: "text", nullable: false),
                    ContentSize = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: true),
                    DatePublished = table.Column<string>(type: "text", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: true),
                    CharCount = table.Column<int>(type: "integer", nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: true),
                    NumberOfPages = table.Column<int>(type: "integer", nullable: false),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    DiscourseType = table.Column<string>(type: "text", nullable: false)
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
                name: "FileTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileMetadataId = table.Column<int>(type: "integer", nullable: false),
                    TagKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TagValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileTags_FileMetadata_FileMetadataId",
                        column: x => x.FileMetadataId,
                        principalTable: "FileMetadata",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoundFileRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SoundDatasetId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ContentUrl = table.Column<string>(type: "text", nullable: false),
                    EncodingFormat = table.Column<string>(type: "text", nullable: false),
                    ContentSize = table.Column<long>(type: "bigint", nullable: true),
                    DateModified = table.Column<string>(type: "text", nullable: true),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: false),
                    SampleRate = table.Column<double>(type: "double precision", nullable: false),
                    Channels = table.Column<int>(type: "integer", nullable: false),
                    Bitrate = table.Column<double>(type: "double precision", nullable: false),
                    IntegratedLoudness = table.Column<double>(type: "double precision", nullable: true),
                    SignalToNoiseRatio = table.Column<double>(type: "double precision", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "FileAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileMetadataId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsCheckedBySupervisor = table.Column<bool>(type: "boolean", nullable: false),
                    CheckedBySupervisorId = table.Column<int>(type: "integer", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupervisorNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileAssignments_FileMetadata_FileMetadataId",
                        column: x => x.FileMetadataId,
                        principalTable: "FileMetadata",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentSupervisors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SupervisorId = table.Column<int>(type: "integer", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSupervisors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentSupervisors_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentSupervisors_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentSupervisors_Users_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Keywords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Keywords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Keywords_FileRecords_FileId",
                        column: x => x.FileId,
                        principalTable: "FileRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Themes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Themes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Themes_FileRecords_FileId",
                        column: x => x.FileId,
                        principalTable: "FileRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_collections_availability",
                table: "Collections",
                column: "Availability");

            migrationBuilder.CreateIndex(
                name: "idx_collections_collected",
                table: "Collections",
                column: "Collected");

            migrationBuilder.CreateIndex(
                name: "idx_collections_language",
                table: "Collections",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "idx_collections_processing",
                table: "Collections",
                column: "Processing");

            migrationBuilder.CreateIndex(
                name: "idx_collections_type",
                table: "Collections",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "idx_datasetaccessapplications_submitted",
                table: "DatasetAccessApplications",
                column: "SubmittedAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_FileAssignments_FileMetadataId_UserId",
                table: "FileAssignments",
                columns: new[] { "FileMetadataId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileAssignments_IsCompleted",
                table: "FileAssignments",
                column: "IsCompleted");

            migrationBuilder.CreateIndex(
                name: "IX_FileAssignments_UserId",
                table: "FileAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_FileName",
                table: "FileMetadata",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_Status",
                table: "FileMetadata",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_filerecords_author",
                table: "FileRecords",
                column: "Author");

            migrationBuilder.CreateIndex(
                name: "idx_filerecords_dataset",
                table: "FileRecords",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "idx_filerecords_date",
                table: "FileRecords",
                column: "DatePublished");

            migrationBuilder.CreateIndex(
                name: "IX_FileTags_FileMetadataId",
                table: "FileTags",
                column: "FileMetadataId");

            migrationBuilder.CreateIndex(
                name: "IX_FileTags_TagKey",
                table: "FileTags",
                column: "TagKey");

            migrationBuilder.CreateIndex(
                name: "idx_keywords_file",
                table: "Keywords",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "idx_keywords_value",
                table: "Keywords",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "idx_soundfilerecords_contenturl",
                table: "SoundFileRecords",
                column: "ContentUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_soundfilerecords_dataset",
                table: "SoundFileRecords",
                column: "SoundDatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSupervisors_AssignedByUserId",
                table: "StudentSupervisors",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSupervisors_IsActive",
                table: "StudentSupervisors",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSupervisors_StudentId_SupervisorId",
                table: "StudentSupervisors",
                columns: new[] { "StudentId", "SupervisorId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentSupervisors_SupervisorId",
                table: "StudentSupervisors",
                column: "SupervisorId");

            migrationBuilder.CreateIndex(
                name: "idx_themes_file",
                table: "Themes",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "idx_themes_value",
                table: "Themes",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "DatasetAccessApplications");

            migrationBuilder.DropTable(
                name: "FileAssignments");

            migrationBuilder.DropTable(
                name: "FileDownloadLogs");

            migrationBuilder.DropTable(
                name: "FileTags");

            migrationBuilder.DropTable(
                name: "Keywords");

            migrationBuilder.DropTable(
                name: "SoundFileRecords");

            migrationBuilder.DropTable(
                name: "StudentSupervisors");

            migrationBuilder.DropTable(
                name: "Themes");

            migrationBuilder.DropTable(
                name: "FileMetadata");

            migrationBuilder.DropTable(
                name: "SoundDatasets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "FileRecords");

            migrationBuilder.DropTable(
                name: "Datasets");
        }
    }
}
