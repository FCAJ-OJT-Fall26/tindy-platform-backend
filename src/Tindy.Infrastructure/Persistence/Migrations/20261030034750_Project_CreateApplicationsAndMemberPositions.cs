using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Project_CreateApplicationsAndMemberPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "position_id",
                table: "project_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_project_positions_position_id_project_id",
                table: "project_positions",
                columns: new[] { "position_id", "project_id" });

            migrationBuilder.CreateTable(
                name: "project_applications",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_applications", x => x.application_id);
                    table.CheckConstraint("ck_project_applications_status", "status IN ('pending','accepted','rejected','withdrawn')");
                    table.ForeignKey(
                        name: "FK_project_applications_project_positions_position_id_project_~",
                        columns: x => new { x.position_id, x.project_id },
                        principalTable: "project_positions",
                        principalColumns: new[] { "position_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_applications_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "project_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_applications_users_applicant_id",
                        column: x => x.applicant_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_applications_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_project_members_position_id_project_id",
                table: "project_members",
                columns: new[] { "position_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_applications_applicant",
                table: "project_applications",
                columns: new[] { "applicant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_project_applications_position_id_project_id",
                table: "project_applications",
                columns: new[] { "position_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_applications_review",
                table: "project_applications",
                columns: new[] { "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_project_applications_reviewed_by",
                table: "project_applications",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ux_project_applications_pending",
                table: "project_applications",
                columns: new[] { "project_id", "applicant_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.AddForeignKey(
                name: "FK_project_members_project_positions_position_id_project_id",
                table: "project_members",
                columns: new[] { "position_id", "project_id" },
                principalTable: "project_positions",
                principalColumns: new[] { "position_id", "project_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_members_project_positions_position_id_project_id",
                table: "project_members");

            migrationBuilder.DropTable(
                name: "project_applications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_project_positions_position_id_project_id",
                table: "project_positions");

            migrationBuilder.DropIndex(
                name: "IX_project_members_position_id_project_id",
                table: "project_members");

            migrationBuilder.DropColumn(
                name: "position_id",
                table: "project_members");
        }
    }
}
