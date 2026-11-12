using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectionReasonAndNudgeToApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_project_applications_status",
                table: "project_applications");

            migrationBuilder.DropIndex(
                name: "ux_notifications_invitation_event_recipient",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "project_applications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() + interval '10 days'");

            migrationBuilder.Sql("UPDATE project_applications SET expires_at = created_at + interval '10 days';");

            migrationBuilder.AddColumn<DateTime>(
                name: "nudged_at",
                table: "project_applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "project_applications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "invitation_id",
                table: "notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "application_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_applications_expires_at",
                table: "project_applications",
                column: "expires_at",
                filter: "status = 'pending'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_applications_status",
                table: "project_applications",
                sql: "status IN ('pending','accepted','rejected','withdrawn','expired')");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_application_event_recipient",
                table: "notifications",
                columns: new[] { "application_id", "type", "recipient_user_id" },
                unique: true,
                filter: "application_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_invitation_event_recipient",
                table: "notifications",
                columns: new[] { "invitation_id", "type", "recipient_user_id" },
                unique: true,
                filter: "invitation_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('invitation_received','invitation_accepted','invitation_declined','invitation_cancelled','application_nudged')");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_project_applications_application_id",
                table: "notifications",
                column: "application_id",
                principalTable: "project_applications",
                principalColumn: "application_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_project_applications_application_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_project_applications_expires_at",
                table: "project_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_project_applications_status",
                table: "project_applications");

            migrationBuilder.DropIndex(
                name: "ux_notifications_application_event_recipient",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ux_notifications_invitation_event_recipient",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "project_applications");

            migrationBuilder.DropColumn(
                name: "nudged_at",
                table: "project_applications");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "project_applications");

            migrationBuilder.DropColumn(
                name: "application_id",
                table: "notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "invitation_id",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_project_applications_status",
                table: "project_applications",
                sql: "status IN ('pending','accepted','rejected','withdrawn')");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_invitation_event_recipient",
                table: "notifications",
                columns: new[] { "invitation_id", "type", "recipient_user_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('invitation_received','invitation_accepted','invitation_declined','invitation_cancelled')");
        }
    }
}
