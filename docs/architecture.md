# Architecture — implemented first vertical slice

## Scope and boundaries

ImageFlow is a reusable image-generation engine, implemented as a modular monolith with assembly boundaries. API and worker still run in one process. There is no story/world logic.

| Project | Responsibility | Dependencies |
| --- | --- | --- |
| `ImageFlow.Domain` | Plan, Prompt, Job, GenerationTask and Attempt entities | .NET only |
| `ImageFlow.Application` | PromptPlanService, JobService, repository/provider interfaces, configuration and queue coordination | Domain |
| `ImageFlow.Infrastructure` | EF SQLite unit of work, repository, migrations, Mock/OpenAI HTTP adapters | Application |
| `ImageFlow.Worker` | Durable dispatch, concurrency, attempts, recovery and output persistence | Infrastructure |
| `ImageFlow.Api` | Composition root, request DTOs, grouped endpoints, HTTP result mapping and read queries | Application, Infrastructure, Worker |

Command endpoints delegate validation and orchestration to application services. `IGenerationRepository` is a scoped unit of work implemented with EF in Infrastructure. Simple read endpoints query EF directly. Queue coordination is shared between services and the worker; provider requests never hold the gate. Migration identities and the database schema are preserved across the assembly move.

### Angular feature structure

```text
app/
  app.ts                          # Router shell only
  app.routes.ts                   # Lazy feature route
  core/
    models/generation.models.ts   # Typed API contracts and status unions
    data-access/generation-api.service.ts # Angular HttpClient + SSE observable
  features/workspace/
    workspace-page.ts             # Store/component wiring
    data-access/workspace.store.ts # NgRx Signal Store, commands and derived state
    components/
      prompt-planner/
      prompt-editor/
      job-queue/
      image-gallery/
  shared/styles/workspace.css
```

NgRx Signal Store 22.0.1 owns plans, jobs, settings, selected plan, busy/error state and connection status. Computed selectors derive gallery items and queued plans. Components use OnPush and read-only signal inputs/output events; edits update immutable drafts through Store methods. The data-access service owns HTTP and EventSource, and Store lifecycle hooks close SSE on scope destruction. The workspace is lazily loaded. Framework guidance: [NgRx Signal Store](https://ngrx.io/guide/signals/signal-store).

## Data flow

1. `POST /api/v1/plans` invokes the selected prompt provider synchronously, validates the response and commits a Plan and its Prompts to SQLite.
2. Prompts may be edited until enqueue. `POST /jobs` atomically creates a Job and Pending tasks with immutable prompt snapshots. A unique PlanId job index makes repeated enqueue idempotent.
3. A single background scheduler selects Pending or due RetryWaiting tasks belonging to Active jobs. A process-wide async gate serializes queue writes and API controls; no gate is held across a provider call.
4. The worker commits Running and an Attempt **before** sending the image request. A bounded set of outstanding async requests enforces configured concurrency, not thread count.
5. On success, the worker writes an image via a temporary file and move, then records the path, Completed state, provider request ID and measured raw usage JSON in SQLite.
6. Angular receives SSE current-state snapshots each second and reloads job details. Images are served by task ID; user-supplied paths are not accepted.

EF Core SQLite migrations apply at startup. Tables: Plans, Prompts, Jobs, Tasks and Attempts. SQLite is the queue authority; there is no in-memory queue to lose across restarts. Images live on the local filesystem. All database writes are awaited.

## States and recovery

Jobs: Active, Paused, Cancelled, Completed, NeedsAttention. Tasks: Pending, Running, Completed, Failed, RetryWaiting, Cancelled, Unknown. Pausing is represented at the owning job, keeping pending tasks ready for resume.

On startup, Running tasks and Running attempts become Unknown. Pending work and persisted retry deadlines are retained. Active jobs containing only terminal tasks are reconciled to Completed or NeedsAttention. Unknown tasks are never automatically sent again. This also applies to connection loss, timeouts, ambiguous 408/5xx responses, malformed successful image responses and local image-write failure.

Only explicit 429 rejections retry: persisted next-attempt time is the larger of exponential backoff and Retry-After (delta/date), bounded by configured attempt count. Other definitive HTTP rejections fail. This intentionally favors avoiding duplicate charges over aggressive automatic recovery.

Pause/cancel stop new requests; already-sent requests finish and keep results. A cancellation response therefore does not imply remote cancellation. If the process crashes after writing an image but before committing completion, an orphan file may exist; provider records/files must be reconciled manually. Exactly-once external execution is not promised.

## Configuration and security

Startup configuration selects Mock, OpenAI or an explicitly configured OpenAICompatible service, explicit supported model capabilities, image size, 1–8 concurrent image requests and 1–5 attempts. OpenAI mode always uses the official endpoint. OpenAICompatible mode requires a configured HTTPS base URL without embedded credentials, query or fragment. Redirects are disabled for provider requests. Credentials come from `OPENAI_API_KEY` or the ignored API-local `appsettings.Local.json`, which is excluded from build/publish output. Environment and command-line settings override the local file. Public settings responses contain no API key. Structured JSON logs exclude prompts, credentials and raw provider error bodies.

The application is for a single localhost API process and one SQLite database. Local CORS permits the Angular development origin. There is no authentication yet; remote exposure requires authentication and deployment hardening. OpenAPI documents the versioned API. SSE is a resumable state snapshot feed, not an append-only event ledger.

## Usage and next increments

Image attempts retain measured provider usage JSON and request IDs; Mock does not invent token counts or charges. No dollar cost is displayed. Planning usage and durable planning attempts, estimated-cost accounting, monetary reservations/caps, continuous bounded generation, runtime persisted concurrency controls, pagination, priority scheduling and manual reconciliation APIs remain future increments. Continuous mode must not be enabled until explicit safety caps are implemented.

## Verification

Backend integration tests use isolated physical SQLite databases and mock providers. NgRx Store unit tests cover immutable drafts, persistence ordering/idempotency, API error recovery and SSE teardown. They cover planning/editing/generation/download, idempotency, state controls, restart behavior, concurrency, Retry-After, Unknown safety and SSE. HTTP-handler tests verify the OpenAI adapters without network calls. Playwright exercises the Angular-to-API workflow using Mock mode and checks persistence after refresh, error display and mobile overflow. The separately invoked `scripts/test-provider.py --allow-paid` runs one real persisted plan/image flow and never retries; it is not part of automated tests. See README for reproducible commands.
