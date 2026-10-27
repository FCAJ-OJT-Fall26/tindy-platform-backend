# Local PDF extraction verification

This opt-in console runner defaults to the production `AiListExtractionService`; `--full-profile` exercises the parallel `AiProfileService` with scalar and list stages. It uses the configured Bedrock provider and the active PostgreSQL catalog. It reads Development User Secrets using the API project's existing ID. It performs no database writes and creates no user account/JWT. HTTP authentication, multipart validation, quotas, and response compatibility are covered separately by `AiProfileHttpTests`.

Run commands from `backend/`. The project is deliberately outside the solution and ordinary `dotnet test` never invokes AWS.

```sh
dotnet build tests/Manual/AiListExtraction
dotnet run --project tests/Manual/AiListExtraction -- --self-test
```

`--self-test` is offline and checks that the reviewer rejects unsupported database skills, invented certificate dates, wrong identity and inferred preferred roles.

## Live verification

Live calls send your document to the configured Amazon Nova model and incur Bedrock charges. Only use a document you are permitted to send. The explicit `--live` switch is required. There is no automatic retry; provider/validation errors stop the run. Semantic mismatches are reported for each requested run. Keep credentials only in User Secrets/environment variables, never command arguments or expectation files.

Create an expectations JSON **outside the repository**. Required skills are the exact reference skill set for that document and active catalog; update expectations when the catalog changes (for example, when an explicit SQL entry is added). Forbidden skills are checked in both overall skills and experiences. Each experience is matched by employer in the final description; its canonical catalog role and dates must match and its project name must be null. Every nonnull role title must exactly match an active database role name. Optional `sourceRole` verifies that the original title/seniority is retained in the description when different from the catalog name. Each certificate is matched by title and must have null issue/expiry dates for this year-only reference-document scenario. This is a reference-CV reviewer, not a universal claim that certificates should never have dates.

```json
{
  "requiredSkills": ["Python"],
  "forbiddenSkills": ["PostgreSQL", "MySQL", "SQL Server"],
  "experiences": [
    {"employer":"Example Company","role":"Developer","startDate":"2021-01","endDate":null,"isOngoing":true}
  ],
  "certificates": [{"title":"Example Certificate"}]
}
```

```sh
DOTNET_SYSTEM_NET_DISABLEIPV6=1 dotnet run --project tests/Manual/AiListExtraction -- \
  --live \
  --pdf-file pdf/CV_Nguyen_Khanh_Ngoc.pdf \
  --expectations ../AI_LIST_EXTRACTION_EXPECTATIONS.json \
  --runs 3 \
  --report ../AI_LIST_EXTRACTION_VERIFICATION.json
```

- `--pdf-file`: one local PDF, nonempty, correct PDF signature, up to 4 MiB. File is read into memory; it is not copied into fixtures.
- `--expectations`: local JSON rules for the reference document.
- `--runs`: 1–3 independent live requests, default 1. Lists use one model call/request; full profile uses two parallel model calls/request (up to six for three requests).
- `--full-profile`: opt in to the combined production service. Default remains list-only. Requires a nonempty `profile` expectation object; `profileContains` optionally checks literal evidence keywords in selected strings.
- `--report`: optional path outside the backend repo, checked before any live call; its directory must already exist.
- IPv4 environment setting is needed on the development machine where .NET IPv6 connections to external APIs time out; it is not a production code change.

Output includes skill names, counts, structured experience role/date checks, warning field/code, metadata, and detailed review failures. It excludes API keys, raw PDFs, descriptions and raw model responses. Reports belong outside Git. Exit code 0 means all requested runs passed the declared review rules; it does not establish accuracy on every document. Inspect and extend expectations for different reference documents instead of weakening checks until a model response passes.

## What the reference checks establish

The current reference rules check the exact overall skill set, active/unique UUIDs, forbidden database-product inferences in all skill lists, company association, role/month ranges, certificate coverage/date precision, null scalar profile fields, and a complete tool stop. The experience skill-name list is included for manual review. A passing report does not prove every skill belongs to the particular employment entry: the name/evidence check cannot independently re-read the PDF or verify quote provenance. Never treat model suggestions as automatically verified or save them without review.

## Full-profile mode

Add reference-document scalar checks to the external expectations JSON. `profile` keys are existing snake_case profile fields, with an expected string, integer or null; strings compare case/accent/punctuation-insensitively. Use null for unknown university/hours/links and for a preferred role not explicitly requested. Contact details must come from the candidate contact card; do not discard them just because the email domain resembles a CV site. `profileContains` checks normalized literal keywords without asserting that a free-form summary matches a fixed sentence.

```json
{
  "profile": {
    "display_name": "Example Person",
    "preferred_role_id": null,
    "available_hours_per_week": null,
    "github_url": null
  },
  "profileContains": {"learning_goals": ["Computer Vision"]}
}
```

These properties supplement the existing required skill/experience/certificate expectations above; they are not a complete expectations file by themselves.

```sh
DOTNET_SYSTEM_NET_DISABLEIPV6=1 dotnet run --project tests/Manual/AiListExtraction -- \
  --live --full-profile \
  --pdf-file pdf/CV_Nguyen_Khanh_Ngoc.pdf \
  --expectations ../AI_PROFILE_EXTRACTION_EXPECTATIONS.json \
  --runs 3 \
  --report ../AI_PROFILE_EXTRACTION_VERIFICATION.json
```

Full mode checks both complete tool stops, list rules and declared scalar facts/nulls. Reports include checked scalar field names and a pass flag without printing actual contact values. Both stages must succeed; a provider/schema error stops subsequent requests. HTTP contract tests separately verify the unchanged four-group body, original error codes/raw-output fields, stage diagnostics and Development-only disclosure. Existing frontend typecheck/build/browser tests run unchanged; frontend Profile builder UI is not wired to scalar values by this backend change.

The list tool now receives active role references in addition to skill references. Each experience selects `role_key` and copies `role_title_source` internally; the API still returns only the canonical `role_title` string/null. Update older reference expectations that previously required literal Junior/intern titles: use the database role family in `role`, and the literal original title in `sourceRole` when it must be preserved. With no catalog equivalent, expect `role: null` plus the preserved source title. Skill evidence and preferred-role evidence rules are unchanged.
