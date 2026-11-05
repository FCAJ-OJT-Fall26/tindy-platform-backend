using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Chat_AddDirectPairAndReadWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direct_key",
                table: "conversations",
                type: "character varying(65)",
                maxLength: 65,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_read_message_id",
                table: "conversation_participants",
                type: "uuid",
                nullable: true);

            // Preserve all histories. A duplicate pair fails the unique-index creation for manual review.
            migrationBuilder.Sql("""
                UPDATE conversations c SET direct_key = pairs.pair_key
                FROM (
                    SELECT conversation_id,
                        string_agg(replace(user_id::text, '-', ''), ':' ORDER BY replace(user_id::text, '-', '')) AS pair_key
                    FROM conversation_participants GROUP BY conversation_id HAVING count(*) = 2
                ) pairs
                WHERE c.conversation_id = pairs.conversation_id AND c.type = 'direct' AND c.status = 'active';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_direct_key",
                table: "conversations",
                column: "direct_key",
                unique: true,
                filter: "type = 'direct' AND status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_direct_key",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "direct_key",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "last_read_message_id",
                table: "conversation_participants");
        }
    }
}
