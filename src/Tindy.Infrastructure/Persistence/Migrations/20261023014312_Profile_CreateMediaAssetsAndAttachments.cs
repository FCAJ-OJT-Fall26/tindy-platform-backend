using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Profile_CreateMediaAssetsAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "avatar_asset_id",
                table: "user_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_oauth_provider",
                table: "user_profiles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_source",
                table: "user_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cover_asset_id",
                table: "user_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_avatar_url",
                table: "external_auth_accounts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_user_certificates_certificate_id_user_id",
                table: "user_certificates",
                columns: new[] { "certificate_id", "user_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_external_auth_accounts_user_id_provider",
                table: "external_auth_accounts",
                columns: new[] { "user_id", "provider" });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    storage_provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    storage_container = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    object_version_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    object_etag = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    reserved_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending_upload"),
                    scan_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "not_required"),
                    scan_provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    scan_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    upload_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delete_requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cleanup_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_cleanup_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_cleanup_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.media_asset_id);
                    table.UniqueConstraint("ak_media_assets_media_asset_id_owner_user_id", x => new { x.media_asset_id, x.owner_user_id });
                    table.CheckConstraint("ck_media_assets_active_safe", "status <> 'active' OR (file_size_bytes IS NOT NULL AND file_size_bytes > 0 AND content_type IS NOT NULL AND reserved_bytes = 0 AND ((content_type = 'application/pdf' AND scan_status = 'clean') OR (content_type <> 'application/pdf' AND scan_status IN ('not_required', 'clean'))))");
                    table.CheckConstraint("ck_media_assets_cleanup_attempts", "cleanup_attempts >= 0");
                    table.CheckConstraint("ck_media_assets_positive_dimensions", "(width IS NULL OR width > 0) AND (height IS NULL OR height > 0)");
                    table.CheckConstraint("ck_media_assets_positive_sizes", "(file_size_bytes IS NULL OR file_size_bytes > 0) AND reserved_bytes >= 0");
                    table.CheckConstraint("ck_media_assets_purpose", "purpose IN ('avatar', 'cover', 'certificate')");
                    table.CheckConstraint("ck_media_assets_scan_provider", "scan_provider IS NULL OR scan_provider IN ('clamav', 'guardduty')");
                    table.CheckConstraint("ck_media_assets_scan_status", "scan_status IN ('pending', 'clean', 'threat_detected', 'unsupported', 'access_denied', 'failed', 'not_required')");
                    table.CheckConstraint("ck_media_assets_status", "status IN ('pending_upload', 'processing', 'active', 'rejected', 'pending_delete')");
                    table.CheckConstraint("ck_media_assets_storage_identity", "btrim(storage_container) <> '' AND btrim(storage_key) <> ''");
                    table.CheckConstraint("ck_media_assets_storage_provider", "storage_provider IN ('local', 's3')");
                    table.ForeignKey(
                        name: "FK_media_assets_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_certificate_attachments",
                columns: table => new
                {
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "owner_only"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_certificate_attachments", x => x.attachment_id);
                    table.CheckConstraint("ck_user_certificate_attachments_visibility", "visibility IN ('owner_only', 'members')");
                    table.ForeignKey(
                        name: "FK_user_certificate_attachments_media_assets_media_asset_id_ow~",
                        columns: x => new { x.media_asset_id, x.owner_user_id },
                        principalTable: "media_assets",
                        principalColumns: new[] { "media_asset_id", "owner_user_id" });
                    table.ForeignKey(
                        name: "FK_user_certificate_attachments_user_certificates_certificate_~",
                        columns: x => new { x.certificate_id, x.owner_user_id },
                        principalTable: "user_certificates",
                        principalColumns: new[] { "certificate_id", "user_id" });
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_avatar_asset_id",
                table: "user_profiles",
                column: "avatar_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_avatar_asset_id_user_id",
                table: "user_profiles",
                columns: new[] { "avatar_asset_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_cover_asset_id",
                table: "user_profiles",
                column: "cover_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_cover_asset_id_user_id",
                table: "user_profiles",
                columns: new[] { "cover_asset_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_user_id_avatar_oauth_provider",
                table: "user_profiles",
                columns: new[] { "user_id", "avatar_oauth_provider" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_profiles_avatar_selection",
                table: "user_profiles",
                sql: "(avatar_source IS NULL AND avatar_asset_id IS NULL AND avatar_oauth_provider IS NULL) OR (avatar_source = 'default' AND avatar_asset_id IS NULL AND avatar_oauth_provider IS NULL) OR (avatar_source = 'uploaded' AND avatar_asset_id IS NOT NULL AND avatar_oauth_provider IS NULL) OR (avatar_source = 'oauth' AND avatar_asset_id IS NOT NULL AND avatar_oauth_provider IN ('Google', 'GitHub'))");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_cleanup",
                table: "media_assets",
                columns: new[] { "status", "next_cleanup_at" },
                filter: "status = 'pending_delete'");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_expiring_uploads",
                table: "media_assets",
                column: "upload_expires_at",
                filter: "status = 'pending_upload'");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_owner_purpose",
                table: "media_assets",
                columns: new[] { "owner_user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_processing",
                table: "media_assets",
                columns: new[] { "status", "created_at" },
                filter: "status = 'processing'");

            migrationBuilder.CreateIndex(
                name: "ux_media_assets_storage_object",
                table: "media_assets",
                columns: new[] { "storage_provider", "storage_container", "storage_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_certificate_attachments_certificate_owner",
                table: "user_certificate_attachments",
                columns: new[] { "certificate_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_certificate_attachments_media_asset_id_owner_user_id",
                table: "user_certificate_attachments",
                columns: new[] { "media_asset_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_certificate_attachments_owner_user_id",
                table: "user_certificate_attachments",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_certificate_attachments_media_asset_id",
                table: "user_certificate_attachments",
                column: "media_asset_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_user_profiles_external_auth_accounts_user_id_avatar_oauth_p~",
                table: "user_profiles",
                columns: new[] { "user_id", "avatar_oauth_provider" },
                principalTable: "external_auth_accounts",
                principalColumns: new[] { "user_id", "provider" });

            migrationBuilder.AddForeignKey(
                name: "FK_user_profiles_media_assets_avatar_asset_id_user_id",
                table: "user_profiles",
                columns: new[] { "avatar_asset_id", "user_id" },
                principalTable: "media_assets",
                principalColumns: new[] { "media_asset_id", "owner_user_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_user_profiles_media_assets_cover_asset_id_user_id",
                table: "user_profiles",
                columns: new[] { "cover_asset_id", "user_id" },
                principalTable: "media_assets",
                principalColumns: new[] { "media_asset_id", "owner_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_profiles_external_auth_accounts_user_id_avatar_oauth_p~",
                table: "user_profiles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_profiles_media_assets_avatar_asset_id_user_id",
                table: "user_profiles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_profiles_media_assets_cover_asset_id_user_id",
                table: "user_profiles");

            migrationBuilder.DropTable(
                name: "user_certificate_attachments");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropIndex(
                name: "ix_user_profiles_avatar_asset_id",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "IX_user_profiles_avatar_asset_id_user_id",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "ix_user_profiles_cover_asset_id",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "IX_user_profiles_cover_asset_id_user_id",
                table: "user_profiles");

            migrationBuilder.DropIndex(
                name: "IX_user_profiles_user_id_avatar_oauth_provider",
                table: "user_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_profiles_avatar_selection",
                table: "user_profiles");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_user_certificates_certificate_id_user_id",
                table: "user_certificates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_external_auth_accounts_user_id_provider",
                table: "external_auth_accounts");

            migrationBuilder.DropColumn(
                name: "avatar_asset_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "avatar_oauth_provider",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "avatar_source",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "cover_asset_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "provider_avatar_url",
                table: "external_auth_accounts");
        }
    }
}
