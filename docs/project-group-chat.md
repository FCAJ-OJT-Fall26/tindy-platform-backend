# Project group chat contract

Each project has one conversation (type: project). Creation and membership changes are atomic with the project operation. Existing projects are backfilled by migration. Group names follow the project name. Completed/archived projects remain writable.

- GET /api/chat/projects/{projectId}: authenticated current/former member resolves its conversation. Outsiders receive 404.
- GET /api/chat/conversations: includes direct and project conversations. Project entries have null other_participant, project_id, name, participants (user_id, display_name, avatar_url, is_leader, is_active, left_at), can_send and left_at.
- Existing messages/send/read endpoints and SignalR SendMessage/MarkRead support both types. Messages include message_type (text/system). Text is 1–4000 trimmed characters. Retry with the same client_message_id and body is idempotent.
- POST /api/chat/projects/{projectId}/leave: leaves the PROJECT, not just its chat. Leader cannot leave. Retry is idempotent.
- DELETE /api/chat/projects/{projectId}/members/{memberId}: leader removes a non-leader PROJECT member. Non-leaders cannot remove others. Retry is idempotent.

Joining through application acceptance/invitation acceptance grants the full history, including periods of absence. Leaving/removal records a UTC cutoff: only messages at/before cutoff are readable/count as unread/markable read. Former members cannot send (403 PROJECT_CHAT_READ_ONLY) or receive new message events. Membership and sends serialize on the project row. Rejoining clears the cutoff. Personal contact blocks apply only to direct chat.

System messages announce joins/departures. Admissions/departures emit chat.requests.changed after commit; foreground/reconnect/15-second resync also recovers missed events. The workspace opens /messages?project={projectId}.

## Apply and verify

From the backend directory, apply migrations before starting the updated API:

    dotnet ef database update --project src/Tindy.Infrastructure --startup-project src/Tindy.Api

The migration creates project groups and participant records for existing projects, including completed/archived projects. Existing direct rooms/messages retain their data. No group messages existed before this migration, so pre-existing inactive memberships receive the migration-time cutoff. A downgrade removes project groups and their messages while retaining direct chat history.

Verification uses disposable PostgreSQL databases through TindyApiFactory/TestPostgres, never the configured application database. ProjectGroupChatHttpTests covers creation, concurrent room resolution, join/rejoin, leaving/removal, visibility/unread/read cutoffs, concurrent sends/departure, invitation admission, personal blocks, archived/completed sends, retries and real SignalR sockets. ProjectGroupChatMigrationTests covers upgrade/backfill/downgrade. Set TINDY_CHAT_BROWSER=1 to run ProjectGroupChatBrowserTests against two headless Chrome sessions and capture desktop/mobile screenshots. Frontend checks: npm run typecheck, npm run build, npm run test:chat.

## Verified on 2026-11-06

- Backend build: passed, zero warnings/errors.
- Full backend suite with TINDY_CHAT_BROWSER=1: 1,042 passed, 0 failed, 2 existing opt-in ClamAV tests skipped (1,044 total). Both the direct-chat and project-group real-browser tests ran.
- Frontend: typecheck and production build passed; all 4 chat-state tests passed. Existing Vite configuration/chunk-size warnings remain.
- Desktop/mobile screenshots are generated in the frontend .tmp-chat-live/screenshots directory by the opt-in browser test.
- Postman CLI 1.73.0 is installed; its command set does not include status. Verification above used the repository's actual HTTP/SignalR/PostgreSQL and Chrome test harnesses.
- Application database migrations were not applied; only disposable test databases were migrated.
