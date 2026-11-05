# Chat requests V1 — implementation and verification

Implemented contract: [CHAT_REQUESTS_API.md](../api/CHAT_REQUESTS_API.md).
Product decisions and acceptance matrix: [MESSAGE_REQUESTS_SPEC.md](MESSAGE_REQUESTS_SPEC.md).

Implemented: request creation/context selection in both directions; accept/reject/cancel; one pending pair and one granted room; immutable opening (500 UTF-16 units); rolling 5/hour and 20/day quotas; idempotency; expiry after 7 days; transactional project/application/position cancellation; block/unblock; legacy room consent; project cards; request inbox/history/pagination and separate badges; SignalR invalidation and reconciliation.

Verification performed on disposable PostgreSQL databases, never the developer database:

- 27 chat-focused cases passed across targeted runs, including HTTP/WebSocket authorization, request races, quotas, auto-cancellation, block/unblock, share retries and migration upgrade/rollback.
- 95 related recruitment/application/position/notification regression cases passed in the broader run.
- Live Chrome test passed with two isolated browser profiles against real Kestrel/PostgreSQL: send opening, accept, realtime exchange, block/unblock, desktop/mobile screenshots. The test waits for loading and send readiness before submitting.
- Frontend TypeScript check, 3 chat state unit tests and Vite production build passed. Existing Vite config and bundle-size warnings remain.
- EF reports no pending model changes relative to the new migration.

The 36-item product matrix remains the reference for additional manual acceptance checks; the counts above describe executed automated cases, not a claim that each matrix row has a separate test.

## Local rollout

1. Review changes and take the normal database backup before schema updates.
2. Stop the running backend before rebuilding: the active Debug process can lock its DLLs. Verification used configuration ChatVerification to avoid interrupting it.
3. From the backend repository, run `dotnet ef database update --project src/Tindy.Infrastructure --startup-project src/Tindy.Api` against the intended development database. This was not run against the developer database during implementation.
4. Restart the backend and frontend together. The old direct-create route now requires prior consent, so deploy the matching client.
5. Confirm existing nonempty rooms remain accessible; empty rooms require requests. Check both directions from project/applicant pages.

No commits or pushes were made for this implementation step. No email, push notifications or changes to actual project membership are part of chat acceptance.
