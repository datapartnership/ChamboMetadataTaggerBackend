using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetadataTagging.Data.Migrations
{
    /// <summary>
    /// Rewrites <c>FileMetadata.BlobName</c> into the canonical storage-key form used by
    /// <c>CollectionFiles.ContentUrl</c> (forward slashes, no leading <c>./</c> or <c>/</c>),
    /// matching <c>CatalogKey.Normalize</c>, and indexes it for the tagger-to-catalog lookup.
    /// </summary>
    public partial class NormalizeBlobNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain SQL understood by both PostgreSQL and SQLite.
            migrationBuilder.Sql("""
                UPDATE "FileMetadata"
                SET "BlobName" = ltrim(
                    CASE WHEN replace("BlobName", '\', '/') LIKE './%'
                         THEN substr(replace("BlobName", '\', '/'), 3)
                         ELSE replace("BlobName", '\', '/')
                    END, '/')
                WHERE "BlobName" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_BlobName",
                table: "FileMetadata",
                column: "BlobName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileMetadata_BlobName",
                table: "FileMetadata");
        }
    }
}
