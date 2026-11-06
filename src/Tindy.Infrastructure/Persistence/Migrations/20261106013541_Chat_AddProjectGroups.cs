using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Chat_AddProjectGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "left_at",
                table: "conversation_participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "message_type",
                table: "chat_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "text");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_project_id",
                table: "conversations",
                column: "project_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_projects_project_id",
                table: "conversations",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "project_id",
                onDelete: ReferentialAction.Cascade);
            // Backfill one room for every existing project, including archived ones.
            migrationBuilder.Sql("""
                INSERT INTO conversations (conversation_id, project_id, type, status, consent_source, created_at, updated_at)
                SELECT gen_random_uuid(), project_id, 'project', 'active', 'membership', created_at, CURRENT_TIMESTAMP
                FROM projects;
                INSERT INTO conversation_participants (conversation_id, user_id, joined_at, left_at)
                SELECT c.conversation_id, m.user_id, m.joined_at,
                       CASE WHEN m.status = 'active' THEN NULL ELSE CURRENT_TIMESTAMP END
                FROM project_members m JOIN conversations c ON c.project_id = m.project_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM conversations WHERE project_id IS NOT NULL;");
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_projects_project_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_project_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "left_at",
                table: "conversation_participants");

            migrationBuilder.DropColumn(
                name: "message_type",
                table: "chat_messages");
        }
    }
}
