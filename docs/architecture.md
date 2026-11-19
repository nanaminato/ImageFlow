# Architecture outline

This document describes the **intended** design, not implemented features.

## Flow

1. Client creates a generation project and prompt planning request.
2. Text-model prompt planner creates structured, distinct prompts in batches.
3. Prompts are saved in SQLite and can be reviewed/edited.
4. Users submit prompts to finite or bounded continuous generation jobs.
5. A durable scheduler coordinates async image generation with concurrency limits, retries, cancellation, and restart recovery.
6. The generated image is written to the configured local storage path; file metadata, prompt, attempts, usage, and cost estimation are recorded in SQLite.
7. ASP.NET Core provides versioned APIs and SSE events to Angular.

## Suggested entities

`GenerationProject`, `GenerationJob`, `GenerationTask`, `Prompt`, `GeneratedImage`, `GenerationAttempt`, `ProviderUsage`, `ApplicationSetting`.

## Suggested boundaries

- **Domain**: entities, valid status transitions, contracts.
- **Application**: orchestration, use cases, planning and scheduling policy.
- **Infrastructure**: OpenAI clients, SQLite, migrations, local filesystem storage.
- **Worker**: durable task execution / recovery.
- **API**: REST, OpenAPI, SSE, local-first security.
- **Angular**: dashboard, prompt editor, jobs, gallery, settings.

## Reliability

Keep SQLite authoritative. Persist attempts and record uncertain outcomes when a remote API call may have completed but no response was received. Use backoff on 429/5xx and heed Retry-After. Distinguish estimated charges from confirmed usage. Enforce finite budget caps on continuous execution.

## Future extension

Add story planners, persistent world state, and visual evaluation as independent clients or plugins consuming public APIs. Avoid hard-coding these concepts into the engine.
