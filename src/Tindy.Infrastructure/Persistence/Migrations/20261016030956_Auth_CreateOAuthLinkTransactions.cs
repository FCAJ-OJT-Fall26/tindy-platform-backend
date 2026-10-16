using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Auth_CreateOAuthLinkTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oauth_link_transactions",
                columns: table => new
                {
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_ticket_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completion_ticket_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_link_transactions", x => x.transaction_id);
                    table.ForeignKey(
                        name: "FK_oauth_link_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oauth_link_transactions_expires_at",
                table: "oauth_link_transactions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_link_transactions_user_id",
                table: "oauth_link_transactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_oauth_link_transactions_completion_hash",
                table: "oauth_link_transactions",
                column: "completion_ticket_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_oauth_link_transactions_start_hash",
                table: "oauth_link_transactions",
                column: "start_ticket_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oauth_link_transactions");
        }
    }
}
