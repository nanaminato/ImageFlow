# ImageFlow implementation roadmap

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
