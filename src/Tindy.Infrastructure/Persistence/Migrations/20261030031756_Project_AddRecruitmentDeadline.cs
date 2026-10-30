using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Project_AddRecruitmentDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "recruitment_closes_at",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            // Preserve the original instant, including midday cutoffs. Convert
            // through UTC so a session's timezone/DST cannot change a "day".
            migrationBuilder.Sql("""
                UPDATE projects
                SET recruitment_closes_at =
                    ((created_at AT TIME ZONE 'UTC') + recruitment_closing_days * INTERVAL '1 day') AT TIME ZONE 'UTC';
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "recruitment_closes_at",
                table: "projects",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_recruitment_closes_at",
                table: "projects",
                column: "recruitment_closes_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // New calendar-based cutoffs generally cannot be represented by the
            // old integer duration. Refuse a lossy downgrade; keep all data intact.
            migrationBuilder.Sql("""
                LOCK TABLE projects IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM projects WHERE recruitment_closes_at IS DISTINCT FROM
                            (((created_at AT TIME ZONE 'UTC') + recruitment_closing_days * INTERVAL '1 day') AT TIME ZONE 'UTC')
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade recruitment deadlines: new or edited cutoffs cannot be represented by legacy day counts. Back up and reconcile deadlines before rollback.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_projects_recruitment_closes_at",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "recruitment_closes_at",
                table: "projects");
        }
    }
}
