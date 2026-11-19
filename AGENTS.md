# ImageFlow — Codex project guidance

## Goal
Build ImageFlow as a reusable **continuous image-generation engine**. Do not build a story-generation product into its core. Higher-level agents, stories and world simulators must consume public application APIs later.

## Technology
- C# / .NET 10, ASP.NET Core Web API, EF Core with SQLite.
- Latest **stable** Angular, TypeScript and Angular Material.
- Prefer a modular monolith and simple single-instance hosting; no distributed infrastructure.
- Use the official OpenAI API. Keep text planning and image generation behind separate provider interfaces. Validate supported model capabilities at runtime/configuration rather than assuming universal options.
- Async/await and bounded concurrency; concurrency is **outstanding API requests**, not threads.

## Development rules
1. Inspect existing files before making changes; implement in small, testable increments.
2. Do not add placeholder functionality and claim it works.
3. Build and test whenever practical; explain any environment limitation.
4. Keep API keys in environment variables or secure local configuration, never in Angular, committed files, logs, or plaintext database settings.
5. Do not send real paid API requests in tests by default; use mock providers.
6. SQLite is the authority for queued tasks, their transitions, retry attempts, prompts and outputs.
7. Persist planned prompts **before** enqueueing image generation.
8. Handle unknown outcomes after network interruption. Never blindly retry an already-sent request when duplicate charges are possible.
9. Honor Retry-After, exponential backoff, configurable concurrency and spending/quantity caps.
10. Implement finite and continuous task modes; a continuous mode must have a bounded safety limit and allow manual stopping.
11. Track separately measured usage and estimated costs. Do not label estimates as actual billed costs.
12. Keep documentation in sync with implementation.

## Suggested layout
```text
src/
  backend/
    ImageFlow.Api/
    ImageFlow.Application/
    ImageFlow.Domain/
    ImageFlow.Infrastructure/
    ImageFlow.Worker/
  frontend/
    imageflow-web/
tests/
  ImageFlow.UnitTests/
  ImageFlow.IntegrationTests/
docs/
```

This layout is a **target**, not an instruction to create empty projects. Create compilable project structures incrementally. API and Worker may run within one process initially.

## Initial API scope
- Plan and review prompts from a theme.
- Generate images from stored prompts.
- Create/list/view/pause/resume/cancel jobs.
- Browse generated images and usage records.
- Stream updates via SSE.

Prefer versioned paths under `/api/v1`.

## Next task
Start from [docs/ROADMAP.md](docs/ROADMAP.md), implement Milestone 1, add automated tests and update the README with reproducible setup steps.
