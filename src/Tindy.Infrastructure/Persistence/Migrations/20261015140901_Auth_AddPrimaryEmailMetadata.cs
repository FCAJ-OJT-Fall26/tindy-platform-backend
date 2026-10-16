using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Auth_AddPrimaryEmailMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add primary email columns
            migrationBuilder.AddColumn<string>(
                name: "primary_email",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_email_normalized",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "primary_email_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // 2. Backfill existing Local User emails
            migrationBuilder.Sql("""
        UPDATE users AS u
        SET primary_email = c.email,
            primary_email_normalized = c.email_normalized,
            primary_email_verified_at = c.email_verified_at
        FROM user_credentials AS c
        WHERE c.user_id = u.user_id
          AND u.primary_email_normalized IS NULL;
        """);

            // 3. Create unique index for primary email
            migrationBuilder.CreateIndex(
                name: "ux_users_primary_email_normalized",
                table: "users",
                column: "primary_email_normalized",
                unique: true);

            // 4. Each user can link one account per provider
            migrationBuilder.CreateIndex(
                name: "ux_external_auth_accounts_user_provider",
                table: "external_auth_accounts",
                columns: new[] { "user_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_primary_email_normalized",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ux_external_auth_accounts_user_provider",
                table: "external_auth_accounts");

            migrationBuilder.DropColumn(
                name: "primary_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "primary_email_normalized",
                table: "users");

            migrationBuilder.DropColumn(
                name: "primary_email_verified_at",
                table: "users");
        }
    }
}
