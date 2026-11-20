# ImageFlow implementation roadmap

## Implementation status — 2026-11-20

Architecture follow-up: backend split into Domain/Application/Infrastructure/Worker/API assemblies; Angular split into a lazy workspace feature, NgRx Signal Store, HTTP/SSE data access and four presentational components. An explicitly configured compatible HTTPS provider is supported alongside the official OpenAI provider. Live testing remains opt-in.

M1 is implemented: runnable .NET 10 API, verified stable Angular/Material 22.3.0, EF Core SQLite migrations, configuration, JSON logs, local CORS, OpenAPI, health, automated backend/browser tests and reproducible README commands.

The first vertical slice also implements selected M2–M4 work: Mock/OpenAI provider interfaces, persisted editable prompt plans, immutable queued prompt snapshots, finite durable jobs, attempt history, bounded configured concurrency, pause/resume/cancel, uncertain-outcome recovery, 429 backoff, REST/SSE and Angular gallery/download.

Live concurrency/settings and bounded continuous generation are implemented. SQLite persists concurrency, the user-supplied per-image USD estimate, per-job quantity/budget snapshots, reservations and stop reasons. Lowering concurrency drains requests. Continuous jobs repeat reviewed stored prompts, stop at quantity/estimated-budget caps, and retain uncertain exposure across restart. Existing data migrates as finite/unpriced history. Tests cover live changes, caps, concurrent reservations, retry refunds, pause/cancel, recovery and upgrade.

Remaining work: priorities and intervals, provider-priced estimation/actual billing reconciliation, planning usage history, richer batching/deduplication, authenticated review attribution/remote output import, authentication and deployment hardening. Finite plans allow 1–20 prompts; continuous jobs require a 1–1000 image cap and a positive estimate budget. Measured image usage is retained separately; estimated exposure is not actual billed cost.

Manual Unknown review is implemented: task detail/history API and UI, evidence-backed one-time conclusions, separate retry authorization, explicit job resume, retained historical attempts and estimate-aware restart-safe recovery. No blind/force retry is provided.

## M1 — Executable foundation
- Create a .NET 10 solution and compilable ASP.NET Core API.
- Create an Angular application using the newest verified **stable** release and Angular Material.
- Set up EF Core SQLite migration, application config, structured logging, CORS for local development, OpenAPI and a health endpoint.
- Keep API and worker in one process to start. Add unit/integration tests and documented setup commands.
- No paid calls on start or in tests.

## M2 — Providers and prompt planning
- `IPromptPlanningProvider`: theme + constraints -> N structured prompts.
- `IImageGenerationProvider`: a stored prompt + model/options -> generated image and usage metadata.
- OpenAI adapters, configurable model choices and capability validation.
- Persist prompt plans before allowing execution; support editable prompt entries, batching and duplicate reduction.
- Mock providers for local development and CI.

## M3 — Durable scheduler
- Persist projects, jobs, tasks, attempts, prompts, images, provider usage and settings to SQLite.
- Finite and continuous job modes; configurable per-provider concurrency, priorities, retry policy and generation intervals.
- Pause/resume/cancel; unexpected restart recovery; careful treatment of in-flight requests with ambiguous outcomes.
- Limit cost, image count and API rate; no uncontrolled loops.
- Task state model: Pending, Running, Completed, Failed, RetryWaiting, Paused, Cancelled, Unknown.

## M4 — Management UI + public API
- Angular dashboard, prompt editor, job list/detail, image gallery, provider settings and cost estimates.
- Versioned REST API and SSE status events.
- Clear error states, status and retry history.

## M5 — Quality and deployment
- Unit and integration tests for concurrency caps, recovery, budget stops, cancellation, idempotency and 429 handling.
- Docker/deployment documentation, production static-file hosting and configuration examples.
- Explicit authentication before exposing the service outside localhost.
- Future upper layers plug into service interfaces without changing the scheduler.

## Acceptance criteria
1. A user enters a theme and receives a persisted set of editable prompts.
2. They queue image generation and watch progress in Angular.
3. They can change concurrency and stop/resume safely.
4. SQLite-backed work survives restart without silent loss or automatic double billing.
5. Generated images and provenance can be inspected and downloaded.
6. Real API testing is opt-in; default CI consumes no OpenAI credits.
