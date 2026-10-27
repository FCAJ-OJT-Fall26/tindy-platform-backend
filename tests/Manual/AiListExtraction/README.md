# Local PDF list extraction verification

This opt-in console runner exercises the production `AiListExtractionService`, the configured Bedrock provider, and the active PostgreSQL skill catalog. It reads Development User Secrets using the API project's existing ID. It performs no database writes and creates no user account/JWT. HTTP authentication, multipart validation, quotas, and response compatibility are covered separately by `AiProfileHttpTests`.

Run commands from `backend/`. The project is deliberately outside the solution and ordinary `dotnet test` never invokes AWS.

```sh
dotnet build tests/Manual/AiListExtraction
dotnet run --project tests/Manual/AiListExtraction -- --self-test
```

`--self-test` is offline and checks that the reviewer rejects unsupported database skills and invented certificate dates.

## Live verification

Live calls send your document to the configured Amazon Nova model and incur Bedrock charges. Only use a document you are permitted to send. The explicit `--live` switch is required. There is no automatic retry; provider/validation errors stop the run. Semantic mismatches are reported for each requested run. Keep credentials only in User Secrets/environment variables, never command arguments or expectation files.

Create an expectations JSON **outside the repository**. Required skills are the exact reference skill set for that document and active catalog; update expectations when the catalog changes (for example, when an explicit SQL entry is added). Forbidden skills are checked in both overall skills and experiences. Each experience is matched by employer in the final description; its role and dates must match and its project name must be null. Each certificate is matched by title and must have null issue/expiry dates for this year-only reference-document scenario. This is a reference-CV reviewer, not a universal claim that certificates should never have dates.

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
- `--runs`: 1–3 independent live invocations, default 1.
- `--report`: optional path outside the backend repo, checked before any live call; its directory must already exist.
- IPv4 environment setting is needed on the development machine where .NET IPv6 connections to external APIs time out; it is not a production code change.

Output includes skill names, counts, warning field/code, metadata, and review failures. It excludes API keys, raw PDFs, descriptions and raw model responses. Reports belong outside Git. Exit code 0 means all requested runs passed the declared review rules; it does not establish accuracy on every document. Inspect and extend expectations for different reference documents instead of weakening checks until a model response passes.
