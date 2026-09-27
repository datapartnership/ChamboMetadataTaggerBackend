using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetadataTagging.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundTranscriptionPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTranscription",
                table: "SoundFileRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptionFileId",
                table: "SoundFileRecords",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_soundfilerecords_istranscription",
                table: "SoundFileRecords",
                column: "IsTranscription");

            // Audio/transcription links are filled in by re-running POST /api/seed/sound.
            migrationBuilder.Sql("""
                UPDATE "SoundFileRecords"
                SET "IsTranscription" = TRUE,
                    "EncodingFormat"  = CASE WHEN lower("EncodingFormat") LIKE 'text/%' THEN "EncodingFormat" ELSE 'text/plain' END
                WHERE lower("ContentUrl") LIKE '%.txt' OR lower("EncodingFormat") LIKE 'text/%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_soundfilerecords_istranscription",
                table: "SoundFileRecords");

            migrationBuilder.DropColumn(
                name: "IsTranscription",
                table: "SoundFileRecords");

            migrationBuilder.DropColumn(
                name: "TranscriptionFileId",
                table: "SoundFileRecords");
        }
    }
}
