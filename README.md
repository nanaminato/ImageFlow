# ImageFlow

A reusable, local-first image-generation engine built with **.NET 10, ASP.NET Core, EF Core SQLite, Angular 22.3.0, Angular Material and NgRx Signal Store 22**. Angular's stable npm `latest` tag was verified on 2026-11-19; the lockfile pins the resolved dependencies.

## Implemented

- Theme → persisted prompt plan → editable review → idempotent finite job → generated image gallery/download.
- Separate prompt-planning and image-generation interfaces, deterministic Mock provider, and official OpenAI HTTP API adapters (Responses + Images).
- SQLite-backed jobs, prompt snapshots, task transitions, attempts, retry deadlines, image paths and raw provider image-usage metadata. Versioned EF migrations apply on startup.
- SQLite-persisted live image concurrency (1–8), pause/resume/cancel, restart recovery, explicit uncertain outcomes, rate-limit backoff and Retry-After.
- Finite and continuous generation with immutable per-job estimates, quantity ceilings and atomic estimated-budget reservations.
- REST, OpenAPI JSON, health checks, SSE job snapshots, structured JSON console logs and local development CORS.
- Angular prompt editor, queue controls, attempt/error details, live progress and image collection.

Mock mode is the default. Its generated images are **labeled SVG previews**, not AI-generated images. Startup and automated tests make no paid requests. OpenAI mode makes paid calls only when you explicitly plan or enqueue work.

## Requirements and setup

Install .NET SDK 10 and Node.js 24.15+ (tested: SDK 10.0.400, Node 24.15.0, npm 11.12.1). Use one API process per database; distributed/multiple-worker hosting is not supported.

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test --no-restore
npm ci --prefix src/frontend/imageflow-web
npm test --prefix src/frontend/imageflow-web
npm run build --prefix src/frontend/imageflow-web
```

Start the API in one terminal:

```powershell
dotnet run --project src/backend/ImageFlow.Api --launch-profile http
```

Start Angular in another:

```powershell
npm start --prefix src/frontend/imageflow-web
```

Open [the UI](http://localhost:4200), enter a theme, plan 1–20 prompts, edit them, then select **Save & queue collection**. Use **Settings** in the header to open the engine-settings dialog for concurrency and the per-image estimate; configuration stays out of the main workspace. Progress and image results update automatically. Existing plans and jobs reload after a refresh. API listens on [localhost:5080](http://localhost:5080); [health](http://localhost:5080/health) and [OpenAPI](http://localhost:5080/openapi/v1.json) are available. Angular's development proxy forwards `/api` to the API.

Default data is `src/backend/ImageFlow.Api/data/`: SQLite database and `images/` files. Keep both together when backing up (stop the API first). Configure an absolute `Engine__DataDirectory` to move it. Never commit data or credentials. Relative data paths resolve from the API content root.

The UI supports English and Simplified Chinese. On first visit, the browser's primary system language selects Chinese for `zh` variants and English otherwise. Use the header language selector to switch immediately; a manual choice is saved in this browser's local storage and takes precedence on later visits. Switching preserves unsaved form contents. If browser storage is blocked, switching still works for the current page. User prompts and raw provider/server errors retain their original language.

### Architecture and navigation

Backend responsibilities now live in real `Domain`, `Application`, `Infrastructure`, `Worker` and `Api` projects. `Program.cs` composes the app; endpoint modules handle HTTP, application services handle planning/job commands, and EF/provider adapters live in Infrastructure. Existing SQLite migrations retain their identities.

Angular uses a lazy workspace route, NgRx Signal Store, a typed Angular HttpClient/SSE service, and separate planner/editor/queue/gallery components. Store state is read-only to components; prompt edits are immutable commands. See [architecture](docs/architecture.md) for the dependency map and file guide.

### Configuration and real OpenAI calls

ASP.NET Core configuration uses `Engine__...` environment variables. `.env.example` documents keys; the application does **not** automatically load `.env` files. Set variables in the API terminal, or use the ignored `src/backend/ImageFlow.Api/appsettings.Local.json` for local configuration. The API explicitly loads this file; environment variables and command-line arguments override it. `OPENAI_API_KEY` takes precedence over a local `Engine:ApiKey`. Local settings are excluded from build and publish output.

```powershell
$env:OPENAI_API_KEY = '<your key>'
$env:Engine__Provider = 'OpenAI'
$env:Engine__PromptModel = 'gpt-4.1-mini'
$env:Engine__ImageModel = 'gpt-image-1.5'
$env:Engine__ImageSize = '1024x1024'
$env:Engine__MaxConcurrency = '2'
$env:Engine__MaxAttempts = '3'
dotnet run --project src/backend/ImageFlow.Api --launch-profile http
```

The adapter explicitly validates supported capabilities rather than accepting arbitrary models/options. This version supports structured prompt planning with `gpt-4.1`, `gpt-4.1-mini`, `gpt-4.1-nano`; PNG generation with `gpt-image-1.5` or `gpt-image-1-mini`, at `1024x1024`, `1536x1024`, or `1024x1536`. Other OpenAI models require an adapter capability update. Account access is validated by OpenAI on explicit requests. API keys are read from the environment or ignored local configuration, never sent to Angular or persisted in SQLite. Provider/model/credential configuration remains server-side and takes effect after restart. Image concurrency and the per-request USD estimate are editable from the header **Settings** dialog and persisted immediately. `Engine__MaxConcurrency` seeds a new database only; saved SQLite settings take precedence on subsequent starts.

Official references: [Angular release policy](https://angular.dev/reference/releases), [OpenAI Responses API](https://developers.openai.com/api/reference/resources/responses/methods/create), [OpenAI Images API](https://developers.openai.com/api/reference/resources/images/methods/generate).

### Explicit third-party compatible service

The official OpenAI provider remains available. To use an OpenAI-compatible gateway, explicitly set `Engine:Provider` to `OpenAICompatible` and configure `Engine:BaseUrl`. Example **without credentials**:

```json
{
  "Engine": {
    "Provider": "OpenAICompatible",
    "BaseUrl": "https://your-provider.example/api/native/v1/",
    "ApiKey": "SET_LOCALLY_ONLY",
    "PromptModel": "gpt-4.1-mini",
    "ImageModel": "gpt-image-1.5"
  }
}
```

Save this only as `src/backend/ImageFlow.Api/appsettings.Local.json` (already ignored). The configured service receives prompts and credentials for its own endpoint. Automatic redirects are disabled. The same explicit model/size validation applies; advertising a model in `/models` alone is not treated as proof of capability. Override `Engine__Provider=Mock` to return to free previews without removing your local settings.

On 2026-11-19, the user-supplied replacement credential was verified against the supplied compatible service: model listing 200, text response 200, and one .NET API plan → persisted job → PNG flow Completed with one image attempt. Returned image usage was 1,399 total tokens, not a billed monetary cost. Credentials and full live results are not committed; local results are under `.cache/live-provider/`.

Re-run only when paid use is explicitly intended:

```powershell
dotnet build
python scripts/test-provider.py --allow-paid
```

This runs at most one persisted plan and one image, with a single allowed image attempt and no automatic resend. It launches a temporary API on localhost:5084 and writes SQLite, PNG, logs and a credential-free report under `.cache/`. In a restricted Windows environment, run it from a normal local terminal if the sandbox's Windows TLS stack cannot acquire credentials. Default backend, Store and browser tests always use Mock/intercepted providers.

### Queue guarantees and limits

Prompts are committed before enqueueing. A plan can create one job; repeating `POST /jobs` returns that job. Queueing freezes prompt editing and snapshots each prompt into its task. To generate another collection, create another plan.

Pause and cancel stop new dispatches. Already-sent requests are allowed to finish and retain their results; cancellation cannot undo provider charges. Pause leaves pending work in place; resume dispatches it. On restart, Pending and RetryWaiting work continues, while Running tasks and attempts become **Unknown**. Unknown/failed jobs become NeedsAttention, with no automatic resend. Use the manual task review workflow below before authorizing any replacement request; there is no claim of exactly-once remote execution.

Only explicit HTTP 429 rejections retry automatically (bounded attempts, exponential backoff, honoring the longer Retry-After delta/date). Network interruption, timeout, 408, 5xx or an output-write failure are uncertain and do not retry automatically. Definitive provider rejections fail. Files are written before completion is committed; a crash between those operations may leave an orphan image for manual reconciliation. No remote call is enclosed in a long SQLite transaction.

Finite plans contain 1–20 prompts. Continuous jobs cycle the saved, immutable prompts and require **both** `maxImages` (1–1000) and a positive `budgetUsd`. There are no repeated text-planning calls. The scheduler creates tasks lazily and stops when either cap is reached; pause/resume/cancel remain available. A plan still creates at most one job.

Set a positive **Estimated USD per image request** in the header **Settings** dialog before enabling continuous mode. Zero means no estimate is configured and continuous mode is rejected. Values accept up to six decimal places. Each job snapshots this estimate, provider/model/size, image-count limit and budget at creation. Later price edits affect new jobs only. Changing provider/model/size across restart stops incompatible queued jobs with NeedsAttention.

Before every image attempt, the same SQLite commit stores the Running task, attempt and estimated USD reservation. Completed and Unknown requests retain their reservation; explicit rejections (including 429) release it, and a retry must reserve again. All outstanding reservations count toward the per-job estimate cap, so simultaneous dispatch cannot overrun it. Continuous Unknown/Failed results stop further dispatch with NeedsAttention. Already-sent calls may finish after a stop and keep their results/reservations. Restart does not reset budgets or quantity counts.

The UI reports **estimated exposure**, including outstanding and uncertain attempts, separately from raw measured provider usage. These are user-supplied estimates, **not actual billed charges or a guarantee of the provider's bill**. The cap applies only to image requests; the explicit planning call before job creation is outside it. Choose estimates appropriate to your provider/model/options and use provider-side billing limits if an actual spending ceiling is required. No pricing is fetched or invented. Old migrated tasks are marked Unpriced and are not retrospectively assigned costs.

Example, after creating a reviewed plan (the numbers below are only test estimates):

```powershell
Invoke-RestMethod http://localhost:5080/api/v1/settings -Method Put -ContentType application/json -Body '{"maxConcurrency":3,"estimatedCostPerImageUsd":0.01}'
Invoke-RestMethod http://localhost:5080/api/v1/jobs -Method Post -ContentType application/json -Body (@{planId=$plan.id;mode='Continuous';maxImages=10;budgetUsd=0.025} | ConvertTo-Json)
```

With this test estimate, at most two images can be dispatched before `BudgetLimit`. A larger estimate budget stops at `QuantityLimit` instead. Terminal limit stops cannot be resumed into an unbounded run. Finite jobs may also supply `budgetUsd`; unsent work is cancelled on budget exhaustion.

Remaining work includes priorities/intervals, text-planning usage history, provider-price-based estimates and billing reconciliation. Planning is synchronous: an interrupted planning response may incur usage without a saved plan; it is never automatically retried.

Local development only: there is no authentication. Keep the service bound to localhost; add authentication and deployment hardening before exposing it to other machines.

## Manual Unknown reconciliation

Expand a task in the queue to inspect its immutable prompt, task/attempt IDs, start/end timestamps, provider/model, provider request ID when available, errors, next automatic retry deadline, measured usage and estimated reservation disposition. The history keeps original Unknown outcomes even after a manual decision.

1. Check the provider's request/output history **and billing records**. The app does not verify these records for you. Wait until all in-flight requests in the same job settle.
2. Record evidence and explicitly confirm one conclusion:
   - `Generated`: mark the task Completed, retain its estimate, and never resend it. This does not import a remote image or manufacture a local image; retrieve missing output from the provider separately.
   - `NotGeneratedNotCharged`: only after confirming both conditions, release that attempt's **estimate**, move the task to Paused and keep the job paused. This is not a billing refund.
   - `Abandon`: cancel this task while retaining uncertain exposure; never resend it.
3. For a confirmed ungenerated/uncharged task, separately select **Authorize reviewed retry** and acknowledge a new potentially charged request. This changes only that task to Pending and records the authorization time. The job stays paused.
4. Resolve other Unknown tasks and authorize any reviewed Paused tasks, then explicitly Resume the job. Existing quantity limits, budget reservations, configured attempt count and provider/model/size compatibility still apply. Cancelled jobs cannot be revived.

Review and retry commands bind to the latest attempt ID. Stale/duplicate submissions return 409; a unique review-per-attempt index and shared queue mutation gate prevent duplicate estimate releases or authorizations. Evidence and review/authorization timestamps persist in SQLite across restart. Conclusions are recorded once; the UI does not offer a force-retry or an override for an unresolved Unknown outcome. Resuming never retries the historical Unknown attempt automatically.

This is a local operator audit trail, not authenticated reviewer identity (authentication remains a deployment prerequisite). Do not paste credentials into evidence. Original prompts, usage and attempt errors remain historical records; review notes do not replace them.

## REST API

All application endpoints use `/api/v1`:

| Method | Path | Purpose |
| --- | --- | --- |
| POST / GET | `/plans` | Plan prompts (`{"theme":"coast","count":3}`) / list plans |
| GET | `/plans/{id}` | Read persisted plan |
| PUT | `/prompts/{id}` | Edit unqueued prompt (`{"text":"..."}`) |
| POST / GET | `/jobs` | Enqueue (`{"planId":"...","mode":"Finite"}` or bounded Continuous options) / list jobs and attempts |
| GET | `/jobs/{id}` | Job details |
| POST | `/jobs/{id}/pause`, `/resume`, `/cancel` | Control dispatch |
| GET | `/images`, `/images/{taskId}` | Image provenance / file |
| GET | `/tasks/{id}` | Task details, attempts and manual reviews |
| POST | `/tasks/{id}/reviews` | Record `{attemptId,resolution,evidence,confirmed:true}` without sending a request |
| POST | `/tasks/{id}/retry` | Authorize `{attemptId,confirmed:true}` after a qualifying review; job stays paused |
| GET | `/usage` | Image attempt history and raw measured usage |
| GET / PUT | `/settings` | Read effective configuration / persist `maxConcurrency` and `estimatedCostPerImageUsd` |
| GET | `/events` | SSE `jobs` snapshot each second; reconnect gets current state |

Example:

```powershell
$plan = Invoke-RestMethod http://localhost:5080/api/v1/plans -Method Post -ContentType application/json -Body '{"theme":"quiet coast","count":2}'
Invoke-RestMethod http://localhost:5080/api/v1/jobs -Method Post -ContentType application/json -Body (@{planId=$plan.id} | ConvertTo-Json)
```

### Browser end-to-end test

```powershell
cd src/frontend/imageflow-web
npx playwright install chromium
npm run test:e2e
```

Playwright launches isolated Mock API/Angular servers, creates a plan, edits a prompt, queues it, waits for images and verifies refresh persistence. Test databases/screenshots live under `.cache/`. Alternatively set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to an installed Chrome executable. Stop existing servers on ports 5080/4200 first. If your Windows sandbox prevents Playwright from shutting down spawned server trees, start the API (with a fresh Mock data directory) and Angular manually, set `IMAGEFLOW_E2E_EXTERNAL=1`, and run the same test. The verified sandbox browser run used this external-server mode.

### Single-process static hosting

```powershell
npm run build --prefix src/frontend/imageflow-web
New-Item -ItemType Directory -Force src/backend/ImageFlow.Api/wwwroot
Copy-Item src/frontend/imageflow-web/dist/imageflow-web/browser/* src/backend/ImageFlow.Api/wwwroot -Recurse -Force
dotnet publish src/backend/ImageFlow.Api -c Release -o .cache/publish
cd .cache/publish
dotnet ImageFlow.Api.dll --urls http://localhost:5080
```

Run the published app from its publish directory as above; set an absolute `Engine__DataDirectory` for a stable production data location. Static assets are included at publish time. Do not commit copied build output.

### Database migrations

```powershell
dotnet tool restore
dotnet ef migrations add YourChange --project src/backend/ImageFlow.Infrastructure --startup-project src/backend/ImageFlow.Api
```

Migrations are committed; startup runs `MigrateAsync`. Tests use isolated real SQLite files, not EF's in-memory provider. The backend test suite covers API flow, validation, idempotency, live concurrency changes, persisted settings, both continuous caps, concurrent reservations, rejection/refund/retry, pause/cancel, restart recovery with retained exposure, historical migration, SSE and intercepted OpenAI contracts. Store and browser tests cover setting persistence, continuous options, estimate stops and error recovery.

### Windows sandbox cache/TLS workaround

If your execution environment blocks the default user cache paths:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.cache/dotnet"
$env:NUGET_PACKAGES = "$PWD/.cache/nuget"
$env:NUGET_HTTP_CACHE_PATH = "$PWD/.cache/http"
$env:npm_config_cache = "$PWD/.cache/npm"
```

In the Codex Windows sandbox, .NET's TLS stack returned `SEC_E_NO_CREDENTIALS`. `scripts/nuget-proxy.py` is an optional localhost-only development bridge using Python's verified HTTPS connection to NuGet. Run it in a separate terminal, then restore using a temporary config pointing at `http://127.0.0.1:8767/https/api.nuget.org/v3/index.json` with `allowInsecureConnections="true"`. The bridge omits repository-signature discovery because NuGet requires that resource to use HTTPS; do not use this workaround as your normal package-source configuration. Standard setup above uses official NuGet HTTPS directly.

See [roadmap](docs/ROADMAP.md) and [architecture](docs/architecture.md) for implementation status and next steps.
