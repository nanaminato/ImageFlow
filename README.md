# ImageFlow

An extensible, persistent AI image-generation task engine.

## Planned stack

- Backend: C# / .NET 10, ASP.NET Core Web API
- Database: SQLite with Entity Framework Core
- Frontend: latest stable Angular and Angular Material
- Image/text providers: OpenAI official API with replaceable provider interfaces
- Background execution: async workers, resumable durable queue, adjustable concurrency

## Scope

ImageFlow starts as a reusable generation engine, **not** a story-writing application. Its first milestones are:

1. Generate and persist structured image prompts from a topic.
2. Queue image generation from those prompts.
3. Support finite and continuous workflows with explicit budget/quantity safety limits.
4. Resume pending work after restart; pause, cancel, retry, and inspect tasks.
5. Provide an API and Angular management UI.

See [docs/ROADMAP.md](docs/ROADMAP.md) for implementation milestones, and [AGENTS.md](AGENTS.md) for Codex guidance.

> Initial repository scaffold only. The application has not yet been implemented.

## Getting started with Codex

Clone this repository and open its root directory in Codex:

```bash
git clone https://github.com/nanaminato/ImageFlow.git
cd ImageFlow
```

Ask Codex to implement the first milestone in `docs/ROADMAP.md` while following `AGENTS.md`. Once initialized, normal development commands should be documented here.
