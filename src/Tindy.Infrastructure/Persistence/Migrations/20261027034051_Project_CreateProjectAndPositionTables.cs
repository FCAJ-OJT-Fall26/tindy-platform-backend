using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Project_CreateProjectAndPositionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tagline = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    difficulty_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "intermediate"),
                    expected_commitment_hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    duration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "3 months"),
                    initial_team_size = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    max_team_capacity = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    color_theme = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "indigo"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "recruiting"),
                    recruitment_closing_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 14),
                    goals = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.project_id);
                    table.CheckConstraint("ck_projects_hours_range", "expected_commitment_hours >= 1 AND expected_commitment_hours <= 40");
                    table.CheckConstraint("ck_projects_team_capacity", "max_team_capacity >= initial_team_size");
                    table.ForeignKey(
                        name: "FK_projects_users_leader_id",
                        column: x => x.leader_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_leader = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_members", x => new { x.project_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "project_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_positions",
                columns: table => new
                {
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    required_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    filled_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    experience_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "intermediate"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "open"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_positions", x => x.position_id);
                    table.ForeignKey(
                        name: "FK_project_positions_professional_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "professional_roles",
                        principalColumn: "professional_role_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_positions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "project_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "position_skills",
                columns: table => new
                {
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    minimum_experience_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_skills", x => new { x.position_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_position_skills_project_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "project_positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_position_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_position_skills_skill_id",
                table: "position_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_user_id",
                table: "project_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_positions_project_id",
                table: "project_positions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_positions_role_id",
                table: "project_positions",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_category",
                table: "projects",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_projects_created_at",
                table: "projects",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_projects_leader_id",
                table: "projects",
                column: "leader_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_status",
                table: "projects",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_skills");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "project_positions");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
