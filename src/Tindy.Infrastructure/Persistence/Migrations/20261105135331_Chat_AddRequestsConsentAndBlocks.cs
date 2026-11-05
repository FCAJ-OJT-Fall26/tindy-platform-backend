using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Chat_AddRequestsConsentAndBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "context_input_key", table: "chat_messages", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "consent_source",
                table: "conversations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "context_position_title",
                table: "chat_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "context_project_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "context_project_name",
                table: "chat_messages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_blocks",
                columns: table => new
                {
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pair_key = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_blocks", x => new { x.blocker_id, x.blocked_id });
                    table.CheckConstraint("ck_chat_blocks_parties", "blocker_id <> blocked_id");
                });

            migrationBuilder.CreateTable(
                name: "chat_requests",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pair_key = table.Column<string>(type: "character varying(65)", maxLength: 65, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_name = table.Column<string>(type: "text", nullable: false),
                    position_title = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    end_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_key = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_requests", x => x.request_id);
                    table.CheckConstraint("ck_chat_requests_body", "char_length(body) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_chat_requests_parties", "sender_id <> recipient_id");
                    table.CheckConstraint("ck_chat_requests_status", "status IN ('pending','accepted','rejected','cancelled','expired')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_blocks_pair_key",
                table: "chat_blocks",
                column: "pair_key");

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_application_id",
                table: "chat_requests",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_pair_key",
                table: "chat_requests",
                column: "pair_key",
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_position_id",
                table: "chat_requests",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_project_id",
                table: "chat_requests",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_recipient_id_created_at",
                table: "chat_requests",
                columns: new[] { "recipient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_sender_id_client_request_id",
                table: "chat_requests",
                columns: new[] { "sender_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_requests_sender_id_created_at",
                table: "chat_requests",
                columns: new[] { "sender_id", "created_at" });
            migrationBuilder.Sql("""
CREATE TABLE chat_request_events (event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, user_ids uuid[] NOT NULL);
CREATE FUNCTION chat_request_event() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 INSERT INTO chat_request_events(user_ids) VALUES (ARRAY[NEW.sender_id, NEW.recipient_id]);
 RETURN NEW;
END $$;
CREATE TRIGGER chat_request_event AFTER INSERT OR UPDATE ON chat_requests FOR EACH ROW EXECUTE FUNCTION chat_request_event();

CREATE FUNCTION chat_refresh_requests(t timestamptz) RETURNS void LANGUAGE sql AS $$
 WITH reasons AS (
  SELECT r.request_id, CASE
   WHEN EXISTS(SELECT 1 FROM chat_blocks b WHERE b.pair_key=r.pair_key) THEN 'blocked'
   WHEN NOT EXISTS(SELECT 1 FROM projects p WHERE p.project_id=r.project_id AND p.status='recruiting' AND p.archived_at IS NULL AND p.recruitment_closes_at>t) THEN 'project_closed'
   WHEN r.application_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM project_applications a WHERE a.application_id=r.application_id AND a.status='pending')
    THEN COALESCE((SELECT 'application_' || a.status FROM project_applications a WHERE a.application_id=r.application_id), 'application_withdrawn')
   WHEN r.position_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM project_positions p WHERE p.position_id=r.position_id AND p.status='open' AND p.filled_count<p.required_count) THEN 'position_closed'
   WHEN r.expires_at<=t THEN 'expired'
   ELSE NULL END reason
  FROM chat_requests r WHERE r.status='pending'
 ) UPDATE chat_requests r SET status=CASE WHEN q.reason='expired' THEN 'expired' ELSE 'cancelled' END,
  end_reason=q.reason, responded_at=t FROM reasons q WHERE r.request_id=q.request_id AND r.status='pending' AND q.reason IS NOT NULL;
$$;

CREATE FUNCTION chat_project_ended() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN
  UPDATE chat_requests SET status='cancelled',end_reason='project_closed',responded_at=clock_timestamp() WHERE project_id=OLD.project_id AND status='pending';
  RETURN OLD;
 END IF;
 IF NEW.status<>'recruiting' OR NEW.archived_at IS NOT NULL OR NEW.recruitment_closes_at<=clock_timestamp()
    OR OLD.status<>'recruiting' OR OLD.archived_at IS NOT NULL OR OLD.recruitment_closes_at<=clock_timestamp() THEN
  UPDATE chat_requests SET status='cancelled',end_reason='project_closed',responded_at=clock_timestamp() WHERE project_id=OLD.project_id AND status='pending';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER chat_project_ended BEFORE UPDATE OR DELETE ON projects FOR EACH ROW EXECUTE FUNCTION chat_project_ended();

CREATE FUNCTION chat_application_ended() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE reason text;
BEGIN
 IF TG_OP='DELETE' THEN reason := 'application_withdrawn';
 ELSE
  IF NEW.status='pending' THEN RETURN NEW; END IF;
  reason := 'application_' || NEW.status;
 END IF;
 UPDATE chat_requests SET status='cancelled',end_reason=reason,responded_at=clock_timestamp() WHERE application_id=OLD.application_id AND (status='pending' OR (status='cancelled' AND end_reason='position_closed' AND responded_at>=transaction_timestamp()));
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;
CREATE TRIGGER chat_application_ended BEFORE UPDATE OR DELETE ON project_applications FOR EACH ROW EXECUTE FUNCTION chat_application_ended();

CREATE FUNCTION chat_position_ended() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN
  UPDATE chat_requests SET status='cancelled',end_reason='position_closed',responded_at=clock_timestamp() WHERE position_id=OLD.position_id AND status='pending';
  RETURN OLD;
 END IF;
 IF NEW.status<>'open' OR NEW.filled_count>=NEW.required_count OR OLD.status<>'open' OR OLD.filled_count>=OLD.required_count THEN
  UPDATE chat_requests SET status='cancelled',end_reason='position_closed',responded_at=clock_timestamp() WHERE position_id=OLD.position_id AND status='pending';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER chat_position_ended BEFORE UPDATE OR DELETE ON project_positions FOR EACH ROW EXECUTE FUNCTION chat_position_ended();

UPDATE conversations c SET consent_source='legacy' WHERE EXISTS (SELECT 1 FROM chat_messages m WHERE m.conversation_id=c.conversation_id);

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "context_input_key", table: "chat_messages");
            migrationBuilder.Sql("""
                DROP TRIGGER chat_project_ended ON projects;
                DROP TRIGGER chat_application_ended ON project_applications;
                DROP TRIGGER chat_position_ended ON project_positions;
                DROP FUNCTION chat_project_ended();
                DROP FUNCTION chat_application_ended();
                DROP FUNCTION chat_position_ended();
                DROP FUNCTION chat_refresh_requests(timestamptz);
                DROP TRIGGER chat_request_event ON chat_requests;
                DROP FUNCTION chat_request_event();
                DROP TABLE chat_request_events;
                """);
            migrationBuilder.DropTable(
                name: "chat_blocks");

            migrationBuilder.DropTable(
                name: "chat_requests");

            migrationBuilder.DropColumn(
                name: "consent_source",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "context_position_title",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "context_project_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "context_project_name",
                table: "chat_messages");
        }
    }
}
