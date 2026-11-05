# Chat request API — V1

Status: implementation contract. Product rules: [MESSAGE_REQUESTS_SPEC.md](../guide/MESSAGE_REQUESTS_SPEC.md).
All routes require an active, onboarded account. REST payloads use snake_case and the existing success envelope/ProblemDetails.

| Method | Route under /api/chat | Behavior |
|---|---|---|
| GET | /request-contexts?user_id={other} | Eligible project/position/application options between caller and target. Target is required. |
| POST | /requests | `{user_id, project_id, position_id?, application_id?, body, client_request_id}`. Returns `{request, conversation_id}`: existing granted room, existing pending, or newly created pending. Reuse client_request_id for identical retries. |
| GET | /requests?direction=received&history=false&page=1 | Page of 30, `items`, `pending_received`, `has_more`. Direction received/sent; history=true includes terminal states. Effective lifecycle is refreshed before returning. |
| POST | /requests/{id}/accept | Recipient only; atomically grant room, record opening message/context and accept once. |
| POST | /requests/{id}/reject | Recipient only. |
| POST | /requests/{id}/cancel | Sender only. |
| GET | /blocks | Caller's own blocks, public name only. |
| PUT/DELETE | /blocks/{user_id} | Set/remove caller's own block. Two-sided blocking remains effective until both sides remove their own entries. |
| POST | /conversations/{id}/context | `{project_id, position_id?, body, client_message_id}` shares a project card in an existing granted room, without request quota. Reuse message ID on retries. |
| POST | /conversations/direct | Opens an already granted room only; otherwise 403 CHAT_REQUEST_REQUIRED. Cannot create consent. |

Errors: 400 VALIDATION_FAILED; 403 CHAT_CONTACT_UNAVAILABLE (block/account unavailable; do not disclose blocker), CHAT_REQUEST_REQUIRED or CHAT_ACTION_FORBIDDEN; 404 CHAT_REQUEST_NOT_FOUND; 409 CHAT_CONTEXT_CLOSED or CHAT_IDEMPOTENCY_CONFLICT; 429 CHAT_REQUEST_LIMIT with retry_at (UTC) and Retry-After. Invalid requests and idempotent retries do not spend quota. Windows are rolling 60m/24h, limits 5/20.

Request DTO includes IDs, other_user (public profile), project/position snapshots, kind (application/opportunity/project_question), body, status, end_reason, timestamps and conversation_id. Block cancellation reason is redacted for sender.
Conversation DTO adds is_blocked/blocked_by_me. Message DTO adds context_project_id/context_project_name/context_position_title. Accepted opening message carries the project card. Never grant membership or change application/invitation on accept.
SignalR event chat.requests.changed invalidates request lists, inbox and blocks across all tabs. Server-side lifecycle triggers plus cleanup worker preserve automatic cancellation; clients also refresh on foreground/reconnect/poll. Delivery is best effort, API state is authoritative.

Migration preserves existing rooms with message history as consent_source=legacy; empty rooms require a request. It does not delete message history. Existing direct-pair migration must already be applied. Deployment requires coordinated backend/frontend update: the old unrestricted direct-create behavior intentionally changes.

Verification: run `dotnet test tests/Tindy.Tests/Tindy.Tests.csproj --filter FullyQualifiedName~Chat` against the isolated test PostgreSQL container. Set `TINDY_CHAT_BROWSER=1` to include Chrome/Kestrel end-to-end verification; otherwise that test is explicitly skipped. The live browser script uses two temporary Chrome profiles and real React/HTTP/SignalR, not a mocked API.

Lifecycle cancellations use PostgreSQL triggers inside project/application/position write transactions. Deadline expiry is also re-evaluated before request operations and by a 5-second worker. The worker drains transactional change events to SignalR; foreground/reconnect/poll reconciles missed events. No email or external notifications are sent.
