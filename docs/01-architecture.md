# 01 — Architecture

This document describes the system-level design of MADAuthor: components, how they talk to each other, the tech stack with rationale, the monorepo layout, and the security and storage models.

## 1. System context

MADAuthor is a single multi-tenant web application. End users are authors, coaches, churches, course creators, and publishers. They use a web browser. Behind the scenes, three runtime components do the work:

1. **Angular SPA** — what the user sees. Talks only to the .NET API. Receives realtime updates via SignalR.
2. **.NET 8 Web API** — owns all reads/writes to the database, all file storage operations, all auth, all SignalR broadcasts, and all *deterministic* background jobs (export rendering, email/SMS notifications, cleanup) via Hangfire.
3. **Claude Code Desktop worker** — runs on Anton's desktop. Wakes on a schedule, polls the `AIJobQueue` table for pending work, executes the multi-stage agentic generation pipeline, writes results back to the database. Never talks to the API directly. See [03-worker-and-job-lifecycle.md](03-worker-and-job-lifecycle.md).

```
                         ┌─────────────────────────┐
                         │      Angular SPA        │
                         │   (apps/web, browser)   │
                         └────────────┬────────────┘
                                      │ HTTPS (REST) + WSS (SignalR)
                                      ▼
        ┌─────────────────────────────────────────────────────────┐
        │                  .NET 8 Web API                         │
        │   ┌──────────┐  ┌──────────┐  ┌────────────────────┐    │
        │   │ Controllers│ │ Hangfire │  │   SignalR Hub      │    │
        │   └──────────┘  └──────────┘  └────────────────────┘    │
        └────────────┬──────────────────────────┬─────────────────┘
                     │ EF Core                  │ Signed URLs
                     ▼                          ▼
        ┌─────────────────────────┐   ┌─────────────────────────┐
        │      SQL Server         │◀──│  Object storage         │
        │   (data + job queue)    │   │  (Azure Blob / S3)      │
        └────────────┬────────────┘   └─────────────────────────┘
                     │ polls AIJobQueue
                     ▼
        ┌─────────────────────────────────────────────────────────┐
        │   Claude Code Desktop worker (Anton's machine)          │
        │   Scheduled task → claim job → run agent pipeline       │
        │   → write chapters/assets back → mark complete          │
        └─────────────────────────────────────────────────────────┘
```

The decoupling matters: the API doesn't know whether AI work is happening. It only knows what's in the database. The worker doesn't know who the user is in real time; it knows what's in the database. This makes both sides independently testable and lets the worker be replaced (hosted service, different model, manual human in the loop) without touching the API.

## 2. What runs where

| Component | Process | Owns |
| --- | --- | --- |
| Angular SPA | Browser | Form input, file selection, navigation, optimistic UI, progress display |
| Controllers | API | Request/response, validation, mapping DTOs ↔ entities |
| Application services | API | Business logic: create book, submit request, claim export, etc. |
| EF Core | API | Schema, queries, migrations |
| Hangfire | API (in-process) | Deterministic background jobs: PDF/EPUB/DOCX export, email/SMS dispatch, blob cleanup, scheduled metrics |
| SignalR hub | API (in-process) | Pushing `JobProgress`, `JobCompleted`, `ExportReady`, `NotificationCreated` events to connected clients |
| Claude Code Desktop | Anton's machine | Agentic generation: planning, writing, editing, continuity, formatting prep |
| Object storage | Azure Blob / S3 | All file blobs: uploads, generated covers, finalized exports |

## 3. Hangfire vs Claude Code worker — what each does

This split is load-bearing. Confusing them produces a system that's hard to reason about.

**Hangfire** handles work that is *deterministic, fast, and runs in-process with the API*:

- Render a `BookProject` to PDF using QuestPDF given finalized chapter content.
- Generate an EPUB given finalized chapters + cover.
- Send an email/SMS notification.
- Clean up orphaned blobs and expired export files.
- Trigger periodic health checks and usage rollups.

**Claude Code Desktop** handles work that is *agentic, slow, and benefits from multi-stage reasoning*:

- Read a `BookRequest` and produce a book plan.
- Expand notes into draft chapters.
- Edit chapters for grammar, flow, and continuity.
- Generate KDP metadata, marketing copy, and dedications.

Concretely: when Claude finishes the generation phases, it enqueues a deterministic Hangfire job (`RenderExportJob`) by inserting a `BookExports` row with `Status = 'Queued'`. The API's Hangfire host picks that up and produces the actual file.

## 4. Tech stack with rationale

| Choice | Rationale | Alternatives considered |
| --- | --- | --- |
| Angular 19 standalone | Anton's pick. Standalone components avoid NgModule boilerplate. Strong typing, mature ecosystem. | React/Next — but stack is Angular-locked. |
| .NET 8 Web API | Long-term-support runtime, EF Core, Hangfire and SignalR are first-class. | .NET 9 — newer, but LTS preferred for a 1-person project. |
| SQL Server | Anton has an existing instance. Dedicated `madapi` database. Strong tooling, mature EF support, atomic job claiming via `UPDATE ... OUTPUT`. | Postgres — would also work, but no reason to switch. |
| Hangfire | First-class .NET in-process job server with a built-in dashboard. Persists to SQL Server. | Quartz.NET (overkill), MassTransit (needs broker). |
| SignalR | First-party realtime over WebSockets, integrates with auth, scales out via Redis backplane when needed. | Server-Sent Events (one-way only), raw WS (no scaling story). |
| QuestPDF | Pure-C# layout engine, no Chromium dependency, MIT license for non-commercial / paid for commercial. | Puppeteer (HTML-to-PDF, heavy), iText (AGPL). |
| DocumentFormat.OpenXml | Microsoft-supported. The DOCX standard. | Aspose (commercial), OpenXmlSDK wrappers. |
| EPUB | Build with a small custom builder + a zip step. EPUB is just structured XHTML in a renamed zip. | Pandoc (external dependency, requires shelling out). |
| Tailwind + Angular Material | Tailwind for layout/typography, Material for accessible primitives. | PrimeNG, Bootstrap. |
| Serilog → Seq (dev) / App Insights (prod) | Structured logs, rich filtering. | NLog, plain `ILogger`. |
| JWT + refresh tokens | Standard for SPA + API. Refresh tokens stored httpOnly. | Cookie-only sessions (works but harder if API ever needs to serve non-SPA clients). |

## 5. Folder layout (monorepo)

```
madauthor/
├── apps/
│   ├── web/                          # Angular 19 SPA
│   │   ├── src/app/
│   │   │   ├── core/                 # auth, http, signalr, guards, interceptors
│   │   │   ├── shared/               # ui primitives, pipes, directives
│   │   │   ├── features/
│   │   │   │   ├── books/            # list, create, edit, detail, preview
│   │   │   │   ├── ai/               # queue, prompt templates
│   │   │   │   ├── publishing/       # exports, platforms
│   │   │   │   ├── assets/           # uploads, covers
│   │   │   │   ├── settings/         # profile, billing, branding
│   │   │   │   └── admin/            # users, ai monitoring, logs
│   │   │   └── layout/               # shell, nav, header
│   │   └── tailwind.config.ts
│   └── api/                          # .NET 8 Web API
│       ├── MadAuthor.Api/            # controllers, program.cs, di
│       ├── MadAuthor.Application/    # mediator handlers (CQRS), validators
│       ├── MadAuthor.Domain/         # entities, value objects, domain events
│       ├── MadAuthor.Infrastructure/ # EF, blob storage, signalr publishers, hangfire jobs
│       └── MadAuthor.Contracts/      # DTOs exposed to TS via NSwag
├── workers/
│   └── claude-desktop/
│       ├── PROMPT.md                 # The standing prompt the cron task feeds Claude
│       ├── claim-job.sql             # Job-claim query the worker runs first
│       ├── progress-update.sql       # Stage/progress writer
│       └── README.md                 # How to set up the CronCreate schedule
├── packages/
│   └── prompts/                      # Versioned agent prompts (markdown, one per agent)
│       ├── planner.md
│       ├── writer.md
│       ├── editor.md
│       └── ...
├── db/
│   ├── migrations/                   # EF Core migration source (under apps/api in practice, mirrored here for visibility)
│   └── seed/                         # Seed scripts: dev users, sample books
├── docs/                             # These docs
└── tools/
    ├── export-dry-run/               # Render a sample book to PDF/EPUB without going through API
    └── prompt-eval/                  # Run a saved BookRequest through the worker locally
```

The split between `MadAuthor.Application` (handlers, validators) and `MadAuthor.Domain` (entities, value objects) is Clean Architecture standard. Worth doing because the spec lists CQRS and a service layer explicitly.

## 6. Security model

**Authentication.** JWT access tokens (15 min) + refresh tokens (14 days, httpOnly cookie, rotated on use). Login, refresh, logout endpoints. MFA via TOTP optional. Password reset via signed token in email link.

**Authorization.** Role-based: `User`, `Author`, `Admin`, `Owner`. Most endpoints require `User`. Tenant-isolated endpoints additionally filter by `CompanyId` from the JWT.

**Multi-tenant isolation.** Every row that should be tenant-scoped carries a `CompanyId`. EF global query filters add `WHERE CompanyId = @currentCompany` to every read. Cross-tenant access from the API is impossible without explicitly removing the filter (only admin-monitoring endpoints do this, audited).

**File access.** Frontend never sees blob storage URLs directly. The API mints time-limited signed URLs (5 min for downloads, 30 min for in-progress uploads). All download URL generations are audited.

**File uploads.** Two-step: client requests a signed PUT URL → uploads directly to blob storage → notifies API with a key. API validates content type and size, kicks off an async virus scan job (Hangfire), and only flips the asset to `Available` after the scan passes. Note: virus-scanning service is an open question — could be Microsoft Defender for Storage (Azure-native), ClamAV in a container, or a third-party API.

**Rate limiting.** ASP.NET `AddRateLimiter` middleware. Per-IP for unauth endpoints (login, register). Per-user for auth endpoints (especially file uploads and AI job submission).

**Audit logging.** Every state-changing action goes through an audit interceptor that writes an `AuditLogs` row with the user id, entity, action, and a JSON diff. Read endpoints are NOT audited by default — too noisy. Admin-monitoring endpoints (which cross tenant boundaries) ARE audited.

**Secrets.** Local dev: .NET user-secrets + `.env.local`. Production: Azure Key Vault or AWS Secrets Manager (depends on hosting).

## 7. Realtime and notifications

**SignalR hub** exposes one hub: `/hubs/notifications`. After connect, the client joins two groups:

- `user:{userId}` — receives notifications meant for this user.
- `project:{projectId}` — joined on demand when the user opens a book detail page; receives `JobProgress` and `ExportReady` events for that project.

**Server-side**, an `INotificationPublisher` is the only allowed way to broadcast. It writes a `Notifications` row, then pushes to SignalR groups. The DB row is the source of truth — if the user is offline, they see the notification next time they connect.

**Channels** beyond in-app: email via DreamHost SMTP using MailKit (`smtp.dreamhost.com:465`, creds in `.env`); SMS (Twilio, when needed); WhatsApp Business API (optional, later). The publisher fans out by user preference.

**Worker → frontend.** The worker doesn't talk to SignalR. It writes progress to `AIJobQueue.Stage` and `AIJobQueue.Progress`. A Hangfire recurring job (every 2s) reads recent progress updates and publishes them to SignalR. This keeps the worker simple — its only contract is the database.

## 8. Storage strategy

**Phase 1 uses local filesystem storage.** Decided 2026-05-20. All blobs live on disk under a configured root (default `C:\Code\madauthor\storage\`). The abstraction is preserved so Azure Blob or S3 can be swapped in without touching consumer code.

**Folders (mirror what would be containers/buckets in cloud storage):**

- `uploads/` — raw user inputs (PDFs, DOCXs, audio). Lifecycle: keep until project is deleted.
- `covers/` — generated covers + final selected cover.
- `exports/` — finalized PDF/EPUB/DOCX. Lifecycle: keep for 90 days after generation, then purge if not downloaded. (MOBI dropped — KDP no longer accepts it for new submissions.)
- `tmp/` — intermediate worker outputs. Lifecycle: delete after 24h via a Hangfire recurring job.

**Path convention:** `{root}/{folder}/{companyId}/{projectId}/{assetId}-{filename}`. Predictable and tenant-isolated even at the storage layer.

**Provider abstraction.** Code uses `IObjectStore` with three implementations: `LocalFileSystemObjectStore` (Phase 1), `AzureBlobObjectStore`, `S3ObjectStore`. Chosen by config. The local store hands the API a relative path; the API serves the file through an authenticated controller endpoint (`GET /api/files/{key}`) rather than via a signed URL — there is no equivalent of a signed URL on the local filesystem. When moving to Azure/S3, this collapses back to true signed URLs.

**Security caveat for the local-storage phase:** download bandwidth goes through the API process, and the storage root must not be web-accessible directly. Don't put it under `wwwroot/`.

## 9. Observability

- **Logs:** Serilog → console + rolling file in dev → Seq locally. Production target TBD (App Insights / OpenTelemetry → Grafana Cloud).
- **Metrics:** `dotnet-counters`-compatible metrics for request rate, latency, Hangfire queue depth, AI job queue depth.
- **Health checks:** `/health/live` (process alive), `/health/ready` (DB reachable, Hangfire OK, blob store reachable). Used by load balancer and Docker healthcheck.
- **Worker visibility:** the worker writes a heartbeat row to a `WorkerHeartbeats` table every poll. The Admin dashboard shows "Last worker heartbeat: 23s ago" so Anton can tell at a glance whether the desktop is awake.

## 10. Open questions

Resolved 2026-05-20 (kept here for traceability):

- ~~SQL Server connection string + DB name~~ — dedicated `madapi` on remote SQL Server, creds in `.env`.
- ~~File storage provider~~ — local filesystem behind `IObjectStore`, swappable later.
- ~~Auth identity store~~ — ASP.NET Identity + JWT.
- ~~Email provider~~ — DreamHost SMTP via MailKit.
- ~~MOBI~~ — dropped from export list.

Still open, deferred to the phase that needs them:

1. **Virus scan service** — defer until Phase 3 when file uploads land. For Phase 3 dev, flag uploads as `Scan=Skipped` and revisit at end of phase.
2. **Cover generation provider** — DALL·E, Stable Diffusion, Midjourney via Discord, or user-uploads-only. Decide at start of Phase 5. Phase 1–4 require user-uploaded covers.
3. **Voice-to-text + OCR providers** — decide at start of Phase 6 (Azure AI Speech / Whisper for voice; Azure Document Intelligence / Tesseract for OCR).
4. **Hosting target for prod** — Azure App Service, container on a VM, or stay local-only. Not blocking until shipping to other users.
5. **Trial / billing model** — out of scope unless commercial launch becomes a priority.
