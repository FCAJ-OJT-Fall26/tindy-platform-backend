using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Profile_AddExperiencesAndCertificatesAndRemoveVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_public",
                table: "user_profiles");

            migrationBuilder.CreateTable(
                name: "profile_experiences",
                columns: table => new
                {
                    experience_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    role_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    repo_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_ongoing = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_experiences", x => x.experience_id);
                    table.CheckConstraint("ck_profile_experiences_dates_order", "start_date IS NULL OR end_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_profile_experiences_month_end", "end_date IS NULL OR EXTRACT(DAY FROM end_date) = 1");
                    table.CheckConstraint("ck_profile_experiences_month_start", "start_date IS NULL OR EXTRACT(DAY FROM start_date) = 1");
                    table.CheckConstraint("ck_profile_experiences_ongoing_end_date", "NOT is_ongoing OR end_date IS NULL");
                    table.ForeignKey(
                        name: "FK_profile_experiences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_certificates",
                columns: table => new
                {
                    certificate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    issuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    credential_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    credential_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    issued_at = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_certificates", x => x.certificate_id);
                    table.CheckConstraint("ck_user_certificates_dates_order", "issued_at IS NULL OR expires_at IS NULL OR expires_at >= issued_at");
                    table.ForeignKey(
                        name: "FK_user_certificates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile_experience_skills",
                columns: table => new
                {
                    experience_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_experience_skills", x => new { x.experience_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_profile_experience_skills_profile_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "profile_experiences",
                        principalColumn: "experience_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_experience_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profile_experience_skills_skill_id",
                table: "profile_experience_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_experiences_user_id_start_date",
                table: "profile_experiences",
                columns: new[] { "user_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_user_certificates_user_id",
                table: "user_certificates",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_experience_skills");

            migrationBuilder.DropTable(
                name: "user_certificates");

            migrationBuilder.DropTable(
                name: "profile_experiences");

            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
