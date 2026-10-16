# Tindy — STEP 05: OAuth Account Linking / Unlinking

> **Scope:** Google + GitHub; JWT Bearer remains default. This change does **not** implement frontend UI, refresh tokens, local-password setup, or onboarding changes. No Docker is required for the scoped unit tests. Backend code is prepared against the STEP 04 source snapshot; review before applying to newer team changes.

## 1. Architecture and decisions

- `User` is central: Local credentials (optional) and multiple external identities.
- A Google/GitHub identity is identified by `(provider, provider_user_id)`, **not email**. No automatic matching or merging by email.
- **Explicit account linking is a 2-stage process.** The OAuth provider callback does **not** link an identity yet; authenticated `POST /api/auth/connections/confirm` does so after checking the SAME JWT UserId as the start request. This protects against provider-account linking CSRF and JWT leakage in URLs.
- `POST start` uses a JWT, then returns a browser `authorization_url` containing a short-lived **opaque** start token, not a JWT.
- The start token is single-use (5 min). The callback returns a separate completion token (90 seconds); both are stored **only as SHA-256 hashes** in PostgreSQL (`oauth_link_transactions`). Completion is consumed atomically. Stored provider identity metadata is transient and removed on success or expiry cleanup.
- Only confirmed provider claims from OAuth middleware are accepted. Browser input cannot supply provider identity.
- `DELETE /connections/{provider}` executes in a SERIALIZABLE PostgreSQL transaction and **cannot remove the user's final login method** (local credential counts as a method). Concurrent unlink conflict returns `409`.
- `GET /connections`, `POST start`, `POST confirm`, and `DELETE` accept a JWT under `OnboardingPolicies.AuthenticatedOnly`. Users still onboarding can manage account connections.
- `Tindy.External` cookie remains temporary and HttpOnly. No provider token or Tindy JWT is written to URLs.
- **Security follow-up:** For production, enforce recent re-authentication for linking/unlinking, consider auditing high-risk account changes, and add tests with real PostgreSQL/Docker in STEP 08. These are not silently added to this MVP step.
- If the provider identity is already linked to a different Tindy user, `409` is returned by STEP 03's `ExternalAuthService.LinkAsync()`. If already linked to this user, linking is idempotent (`created=false`). If the user already linked a *different* identity for the same provider, `409`.
- When a ticket is consumed and an error occurs while linking, the ticket is **not** reusable. Repeat the OAuth linking flow; no silent merge occurs.

## 2. Files changed

| Layer | File | Reason |
|---|---|---|
| API | `Controllers/OAuthConnectionsController.cs` | API start/challenge/callback/confirm/list/unlink |
| API | `Extensions/AuthenticatedExternalIdentityReader.cs` | Shared provider claim validation |
| API | `Controllers/OAuthController.cs` | Reuse reader without changing existing login result |
| API | `Extensions/ServiceCollectionExtensions.cs` | Service DI |
| Application | `DTOs/Authentication/OAuthLinkConfirmRequest.cs` | Class validation (avoids positional-record metadata 500) |
| Application | `Models/Authentication/AccountConnectionModels.cs` | Safe connected account response |
| Application | `Interfaces/Repositories/IAccountConnectionsRepository.cs` | DB read + safe unlink contract |
| Application | `Interfaces/Repositories/IOAuthLinkTransactionStore.cs` | Single-use linking state contract |
| Application | `Interfaces/Services/IAccountConnectionsService.cs` | Business contract |
| Application | `Services/AccountConnectionsService.cs` | Two-stage linking orchestration |
| Infrastructure | `Persistence/Models/OAuthLinkTransaction.cs` | Persistent short-lived state |
| Infrastructure | `Persistence/Configurations/OAuthLinkTransactionConfiguration.cs` | EF mapping/indexes |
| Infrastructure | `Persistence/TindyDbContext.cs` | New DbSet |
| Infrastructure | `Repositories/OAuthLinkTransactionStore.cs` | Atomic start/complete/consume; PostgreSQL |
| Infrastructure | `Repositories/AccountConnectionsRepository.cs` | Snapshot and SERIALIZABLE unlink |
| Infrastructure | `Extensions/ServiceCollectionExtensions.cs` | Repository DI |
| Tests | `Security/AccountConnectionsServiceTests.cs` | Docker-free business tests |

**Not changed:** JWT configuration, Local Register/Login/Logout, `AuthService`, `JwtTokenService`, User/Credential/ExternalAccount entity schema, onboarding. STEP 04 request DTO should remain the **class** with `[Required]` and `[MaxLength(256)]` that you already fixed; this patch does not revert it.

## 3. Apply and build (PowerShell 7, root backend)

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"
git status
git -c core.whitespace=cr-at-eol apply --check .\Tindy_STEP05_Account_Linking_Unlinking.patch
git -c core.whitespace=cr-at-eol apply .\Tindy_STEP05_Account_Linking_Unlinking.patch
dotnet build .\Tindy.slnx
```

Stop on conflicts. Do not overwrite STEP 04 changes or the local fixed `OAuthExchangeRequest.cs`.

## 4. EF Migration required

Generate **on your real repository**, after code builds, so EF updates the current ModelSnapshot and preserves team migrations:

```powershell
dotnet ef migrations add Auth_CreateOAuthLinkTransactions `
  --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
  --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
  --context TindyDbContext `
  --output-dir Persistence/Migrations `
  -- --environment Development
```

Review Up: creates `oauth_link_transactions` with transaction PK, start/completion hash unique indexes, UserId FK and expires index; Down drops it. **Do not drop your database**. After reviewing:

```powershell
dotnet ef database update `
  --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
  --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
  --context TindyDbContext `
  -- --environment Development
```

Optional SQL `psql -U postgres -d tindy` then `\d oauth_link_transactions`.

## 5. API contract

| Method | URL | Authorization | Expected |
|---|---|---|---|
| GET | `/api/auth/connections` | Bearer JWT | `has_local_password` and `providers` |
| POST | `/api/auth/connections/google/start` | Bearer JWT | `authorization_url` |
| POST | `/api/auth/connections/github/start` | Bearer JWT | `authorization_url` |
| GET | `/api/auth/connections/{provider}/challenge?request=...` | Browser (opaque token) | Redirect provider |
| GET | `/api/auth/connections/complete` | Callback temporary cookie | Redirect `#link_ticket=...` |
| POST | `/api/auth/connections/confirm` | Bearer JWT | Link only after UserId matches |
| DELETE | `/api/auth/connections/google` | Bearer JWT | Unlink if a second method exists |
| DELETE | `/api/auth/connections/github` | Bearer JWT | Unlink if a second method exists |

### Browser + Postman manual test

1. Log in **Local** or existing **OAuth** via STEP 04 and obtain a valid **Tindy JWT**; don't paste it into chat. Re-authenticate if JWT expired.
2. Postman `POST https://localhost:7190/api/auth/connections/github/start` with `Authorization: Bearer <JWT>`; copy `data.authorization_url`. This URL contains a single-use opaque token; don't share it.
3. Open the exact `authorization_url` in the browser, choose/authorize GitHub. Callback `/signin-github` is handled by middleware. You may land on an unimplemented frontend route, but address bar should contain `#link_ticket=...`.
4. **Within 90 seconds**, call Postman `POST https://localhost:7190/api/auth/connections/confirm` with `Authorization: Bearer <SAME USER JWT>` and JSON `{"link_ticket":"<FRESH_LINK_TICKET>"}`. Expected HTTP 200, `created=true` if newly linked. If same provider identity linked again through a new flow, `created=false`.
5. `GET /api/auth/connections` with same JWT: provider is listed. Provider must NOT create a second User; existing `user_id` unchanged.
6. `DELETE /api/auth/connections/github` (with JWT) should succeed **only if** Local or another OAuth provider is still linked. If last sign-in method, response is HTTP `409`.
7. Repeat same completion ticket: HTTP `401`. Wrong Tindy JWT: HTTP `401` and must **not** consume the rightful user's ticket.
8. Link a provider identity already associated with a different Tindy User: HTTP `409` (no cross-account linking). Verify no automatic account merging on coincident email.
9. Repeat with Google. Confirm Local Register/Login and STEP 04 Login/Exchange still work.

**Known STEP 07 dependency:** Frontend currently does not automatically exchange `#link_ticket`. Do not use the OAuth login ticket endpoint (`/api/auth/oauth/exchange`) for a link ticket; it is a different flow.

## 6. Docker-free unit tests

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
  --filter "FullyQualifiedName~AccountConnectionsServiceTests"

git -c core.whitespace=cr-at-eol diff --check
git status
git diff --stat
```

The last-method concurrent-unlink invariant and PostgreSQL query semantics should later be tested against PostgreSQL in STEP 08. No Docker requirement for this STEP 05 handoff.

## 7. Definition of Done and commit

- [ ] Code builds; STEP 04 OAuth Login/Register still works.
- [ ] Migration generated, reviewed, applied; new table and indexes exist.
- [ ] Authorized start/challenge and callback produce `link_ticket`.
- [ ] Confirm requires JWT from the **same User** and consumes the link ticket once.
- [ ] Local+Google/GitHub and Google+GitHub linking preserve the same UserId.
- [ ] Linking another User's identity returns 409; no linking by matching email.
- [ ] Unlink cannot remove final login method; same-user re-link is idempotent.
- [ ] Manual tests pass; Docker and integration tests deferred by agreement.
- [ ] Review diff; stage only STEP 05 files and generated migration.

```powershell
git add src/ tests/Tindy.Tests/Security/AccountConnectionsServiceTests.cs docs/guide/OAUTH_STEP05_ACCOUNT_CONNECTIONS.md
git diff --cached --stat
git commit -m "feat(auth): implement oauth account linking and unlinking"
```

The patch file in repository root is an untracked download and should **not** be committed.
