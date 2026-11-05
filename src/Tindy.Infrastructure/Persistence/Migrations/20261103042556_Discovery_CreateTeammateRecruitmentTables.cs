using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Discovery_CreateTeammateRecruitmentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_invitations",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviting_leader_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_invitations", x => x.invitation_id);
                    table.CheckConstraint("ck_project_invitations_distinct_users", "invited_user_id <> inviting_leader_id");
                    table.CheckConstraint("ck_project_invitations_expiry", "expires_at = created_at + interval '168 hours'");
                    table.CheckConstraint("ck_project_invitations_status", "status IN ('pending','accepted','declined','cancelled','expired')");
                    table.CheckConstraint("ck_project_invitations_timestamps", "updated_at >= created_at AND ((status IN ('accepted','declined') AND responded_at IS NOT NULL AND responded_at >= created_at AND responded_at < expires_at AND updated_at = responded_at) OR (status = 'pending' AND responded_at IS NULL AND updated_at = created_at) OR (status = 'expired' AND responded_at IS NULL AND updated_at >= expires_at) OR (status = 'cancelled' AND responded_at IS NULL AND updated_at < expires_at))");
                    table.ForeignKey(
                        name: "FK_project_invitations_project_positions_position_id_project_id",
                        columns: x => new { x.position_id, x.project_id },
                        principalTable: "project_positions",
                        principalColumns: new[] { "position_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "project_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_invitations_users_invited_user_id",
                        column: x => x.invited_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_invitations_users_inviting_leader_id",
                        column: x => x.inviting_leader_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teammate_discovery_interactions",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teammate_discovery_interactions", x => new { x.project_id, x.position_id, x.candidate_user_id });
                    table.CheckConstraint("ck_teammate_discovery_interactions_action", "action IN ('saved','shortlisted','skipped')");
                    table.CheckConstraint("ck_teammate_discovery_interactions_timestamps", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_teammate_discovery_interactions_project_positions_position_~",
                        columns: x => new { x.position_id, x.project_id },
                        principalTable: "project_positions",
                        principalColumns: new[] { "position_id", "project_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_teammate_discovery_interactions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "project_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_teammate_discovery_interactions_users_candidate_user_id",
                        column: x => x.candidate_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_declined",
                table: "project_invitations",
                columns: new[] { "project_id", "invited_user_id", "responded_at" },
                descending: new[] { false, false, true },
                filter: "status = 'declined'");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_inviting_leader",
                table: "project_invitations",
                column: "inviting_leader_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_member_history",
                table: "project_invitations",
                columns: new[] { "invited_user_id", "created_at", "invitation_id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_position",
                table: "project_invitations",
                columns: new[] { "position_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_invitations_project_history",
                table: "project_invitations",
                columns: new[] { "project_id", "created_at", "invitation_id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "ux_project_invitations_pending",
                table: "project_invitations",
                columns: new[] { "project_id", "invited_user_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_teammate_discovery_interactions_position_id_project_id",
                table: "teammate_discovery_interactions",
                columns: new[] { "position_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teammate_interactions_candidate",
                table: "teammate_discovery_interactions",
                column: "candidate_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_teammate_interactions_list",
                table: "teammate_discovery_interactions",
                columns: new[] { "project_id", "position_id", "action", "updated_at", "candidate_user_id" },
                descending: new[] { false, false, false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_invitations");

            migrationBuilder.DropTable(
                name: "teammate_discovery_interactions");
        }
    }
}
