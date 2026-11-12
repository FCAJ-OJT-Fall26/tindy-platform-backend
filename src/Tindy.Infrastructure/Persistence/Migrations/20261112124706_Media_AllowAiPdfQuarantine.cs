using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Media_AllowAiPdfQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_media_assets_purpose",
                table: "media_assets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_assets_purpose",
                table: "media_assets",
                sql: "purpose IN ('avatar', 'cover', 'certificate', 'ai_input')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_media_assets_purpose",
                table: "media_assets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_assets_purpose",
                table: "media_assets",
                sql: "purpose IN ('avatar', 'cover', 'certificate')");
        }
    }
}
