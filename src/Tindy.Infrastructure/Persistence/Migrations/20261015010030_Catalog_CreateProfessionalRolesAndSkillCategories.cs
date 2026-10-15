using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Catalog_CreateProfessionalRolesAndSkillCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "preferred_role_id",
                table: "user_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "skills",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "professional_roles",
                columns: table => new
                {
                    professional_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_roles", x => x.professional_role_id);
                });

            migrationBuilder.CreateTable(
                name: "skill_categories",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_categories", x => x.category_id);
                });

            migrationBuilder.CreateTable(
                name: "skill_category_skills",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_category_skills", x => new { x.category_id, x.skill_id });
                    table.ForeignKey(
                        name: "FK_skill_category_skills_skill_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "skill_categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_skill_category_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_preferred_role_id",
                table: "user_profiles",
                column: "preferred_role_id");

            migrationBuilder.CreateIndex(
                name: "ux_professional_roles_name_normalized",
                table: "professional_roles",
                column: "name_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_skill_categories_name_normalized",
                table: "skill_categories",
                column: "name_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_skill_category_skills_skill_id",
                table: "skill_category_skills",
                column: "skill_id");

            migrationBuilder.AddForeignKey(
                name: "FK_user_profiles_professional_roles_preferred_role_id",
                table: "user_profiles",
                column: "preferred_role_id",
                principalTable: "professional_roles",
                principalColumn: "professional_role_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_profiles_professional_roles_preferred_role_id",
                table: "user_profiles");

            migrationBuilder.DropTable(
                name: "professional_roles");

            migrationBuilder.DropTable(
                name: "skill_category_skills");

            migrationBuilder.DropTable(
                name: "skill_categories");

            migrationBuilder.DropIndex(
                name: "ix_user_profiles_preferred_role_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "preferred_role_id",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "skills");
        }
    }
}
