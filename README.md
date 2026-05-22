# MADAuthor

AI-native book creation and publishing platform. Takes raw input — an idea, an outline, a sermon transcript, a half-written manuscript, voice notes, a stack of PDFs — and produces structured, print-ready, multi-format books (PDF, EPUB, MOBI, DOCX) with covers, metadata, KDP listings, and marketing assets.

## Status: Phase 0 — Architecture sign-off

No code has been written yet. This repository currently contains the architecture documents only.

Read in this order:

1. **[Architecture](docs/01-architecture.md)** — system overview, components, tech-stack rationale, monorepo layout, security model.
2. **[Data model](docs/02-data-model.md)** — schema with refinements over the original spec (deduped flags, FKs, indexes, tenant model).
3. **[Worker and job lifecycle](docs/03-worker-and-job-lifecycle.md)** — how Claude Code Desktop becomes the AI worker by polling SQL Server.
4. **[AI orchestration](docs/04-ai-orchestration.md)** — multi-agent pipeline (Planner → Researcher → Writer → Editor → Continuity → Formatter).
5. **[Roadmap](docs/05-roadmap.md)** — phased delivery plan from skeleton through V1.

## The unusual architectural choice

The AI worker is **Claude Code Desktop** polling SQL Server on a scheduled task — not a hosted background service that calls an Anthropic or OpenAI API. The database is the contract between the .NET API and the worker, and there is no outbound AI call from the API process. This keeps API-key management out of the platform and bills AI usage through the Claude subscription instead. The full pattern is described in [docs/03-worker-and-job-lifecycle.md](docs/03-worker-and-job-lifecycle.md).

## Tech stack at a glance

| Layer | Tech |
| --- | --- |
| Frontend | Angular 19 (standalone), Tailwind CSS, Angular Material, SignalR client |
| API | .NET 8 Web API, EF Core 8, Hangfire, SignalR, JWT + refresh tokens, Serilog |
| Database | SQL Server — dedicated `madapi` database on remote instance (creds in `.env`, gitignored) |
| AI worker | Claude Code Desktop via scheduled task |
| File storage | Local filesystem (Phase 1) behind `IObjectStore`; Azure Blob/S3 swappable later |
| Email | DreamHost SMTP via MailKit (creds in `.env`) |
| Exports | QuestPDF (PDF), EPUB via custom builder, DocumentFormat.OpenXml (DOCX) |
| Observability | Serilog → file + Seq locally; Application Insights or OpenTelemetry for prod |

## What's still undecided

Decisions resolved 2026-05-20: DB = dedicated `madapi`; storage = local filesystem; auth = JWT (with ASP.NET Identity as the user store); email = DreamHost SMTP; MOBI dropped; cover-gen deferred to Phase 5; outline-approval default true.

Still open, deferred until later phases:

- **Hosting target for prod** — Azure App Service, container on a VM, or stay local-only for now. Not blocking Phase 1.
- **Trial vs paid model** — affects billing tables and AI-usage gating. Out of scope unless commercial launch is on the horizon.
- **Virus scanning for uploads** — pick a provider when uploads land in Phase 3.
- **Voice-to-text + OCR** — pick providers for Phase 6.
- **Cover image generation** — pick a provider when Phase 5 begins.

## Repository layout (planned, not yet created)

See [docs/01-architecture.md §6](docs/01-architecture.md) for the full monorepo layout. High level:

```
madauthor/
├── apps/
│   ├── web/                  # Angular 19 SPA
│   └── api/                  # .NET 8 Web API
├── workers/
│   └── claude-desktop/       # Scheduled-task scripts + prompt templates for Claude Code
├── packages/
│   ├── shared-contracts/     # Generated TS DTOs from .NET via NSwag
│   └── prompts/              # Versioned prompt templates (markdown)
├── db/
│   └── migrations/           # EF Core migrations (source of truth for schema)
├── docs/                     # This directory
└── tools/                    # One-off scripts (seed data, export dry-run, etc.)
```
