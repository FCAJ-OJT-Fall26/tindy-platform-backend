using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Profile_AddOnboardingDataAndSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "learning_goals",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_role",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_projects_and_contribution",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_interests",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "technologies_to_learn",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skills", x => x.skill_id);
                });

            migrationBuilder.CreateTable(
                name: "user_onboarding_steps",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_number = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_onboarding_steps", x => new { x.user_id, x.step_number });
                    table.CheckConstraint("ck_user_onboarding_steps_step_number", "step_number BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_user_onboarding_steps_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_skills",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_skills", x => new { x.user_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_user_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_skills_user_profiles_user_id",
                        column: x => x.user_id,
                        principalTable: "user_profiles",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_skills_name_normalized",
                table: "skills",
                column: "name_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_skills_skill_id",
                table: "user_skills",
                column: "skill_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_onboarding_steps");

            migrationBuilder.DropTable(
                name: "user_skills");

            migrationBuilder.DropTable(
                name: "skills");

            migrationBuilder.DropColumn(
                name: "learning_goals",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "preferred_role",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "previous_projects_and_contribution",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "professional_interests",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "technologies_to_learn",
                table: "user_profiles");
        }
    }
}
