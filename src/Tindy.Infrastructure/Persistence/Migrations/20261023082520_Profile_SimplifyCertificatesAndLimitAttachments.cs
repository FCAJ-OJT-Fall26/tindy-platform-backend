using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Profile_SimplifyCertificatesAndLimitAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hold both tables until commit so no upload can race the preflight/index.
            // Fail before dropping legacy columns; never choose or delete an attachment.
            migrationBuilder.Sql("""
                LOCK TABLE user_certificates, user_certificate_attachments IN ACCESS EXCLUSIVE MODE;
                DO $preflight$
                DECLARE
                    duplicate_records text;
                BEGIN
                    SELECT string_agg(
                        format('certificate_id=%s attachment_ids=[%s]', certificate_id, attachment_ids),
                        E'\n' ORDER BY certificate_id)
                    INTO duplicate_records
                    FROM (
                        SELECT certificate_id,
                            string_agg(attachment_id::text, ', ' ORDER BY attachment_id) AS attachment_ids
                        FROM user_certificate_attachments
                        GROUP BY certificate_id
                        HAVING count(*) > 1
                    ) duplicates;

                    IF duplicate_records IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23505',
                            MESSAGE = 'Cannot enforce one attachment per certificate: resolve duplicate attachments manually before retrying.',
                            DETAIL = duplicate_records,
                            HINT = 'Run docs/sql/PROFILE_CERTIFICATES_PREFLIGHT.sql. No attachment was removed.';
                    END IF;
                END $preflight$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_certificates_dates_order",
                table: "user_certificates");

            migrationBuilder.DropColumn(
                name: "credential_id",
                table: "user_certificates");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "user_certificates");

            migrationBuilder.DropColumn(
                name: "issued_at",
                table: "user_certificates");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "user_certificates",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_certificate_attachments_certificate_id",
                table: "user_certificate_attachments",
                column: "certificate_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_user_certificate_attachments_certificate_id",
                table: "user_certificate_attachments");

            migrationBuilder.DropColumn(
                name: "description",
                table: "user_certificates");

            migrationBuilder.AddColumn<string>(
                name: "credential_id",
                table: "user_certificates",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expires_at",
                table: "user_certificates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issued_at",
                table: "user_certificates",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_certificates_dates_order",
                table: "user_certificates",
                sql: "issued_at IS NULL OR expires_at IS NULL OR expires_at >= issued_at");
        }
    }
}
