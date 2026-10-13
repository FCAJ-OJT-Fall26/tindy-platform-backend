using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingStateAndAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_onboarding_completed",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "available_hours_per_week",
                table: "user_profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_profiles_available_hours_per_week",
                table: "user_profiles",
                sql: "available_hours_per_week IS NULL OR available_hours_per_week BETWEEN 1 AND 40");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_profiles_available_hours_per_week",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "is_onboarding_completed",
                table: "users");

            migrationBuilder.DropColumn(
                name: "available_hours_per_week",
                table: "user_profiles");
        }
    }
}
