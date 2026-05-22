# 05 — Roadmap

A realistic phased plan to get from "architecture docs" to "Anton can produce a book end-to-end." Each phase has a goal, a scope, a "done" definition, and an estimate in *focused work sessions* (one session ≈ a couple of hours of concentrated work). Estimates are mine — they slip when reality intervenes.

## Phase 0 — Architecture sign-off

**Goal:** Anton has reviewed the architecture and committed to the design decisions before any code is written.

**Scope:**

- These five documents in `docs/`.
- Memory written about the project and the unusual worker pattern.

**Done when:** Anton reviews and either approves or asks for revisions. No code, no migrations, no Angular CLI, nothing.

**Estimate:** This session.

## Phase 1 — Skeleton + auth + create-book wizard

**Goal:** Anton can open the Angular app, log in, fill out the create-book wizard, hit submit, and a row appears in `BookProjects` and `BookRequests`. No AI work yet.

**Scope:**

- Repo structure created (`apps/web`, `apps/api`, `db`, `workers`, `packages`, `tools`).
- .NET 8 Web API solution: `Api`, `Application`, `Domain`, `Infrastructure`, `Contracts`.
- EF Core: initial migration creates Users, Companies, CompanyMembers, Authors, BookProjects, BookRequests, BookChapters (empty), AIJobQueue (empty), AuditLogs.
- Hangfire wired (empty job set).
- SignalR hub registered, `/hubs/notifications` reachable.
- Auth: register, login, refresh, logout. JWT + httpOnly refresh cookie. Password reset deferred.
- Angular 19 standalone app scaffolded with Tailwind + Material.
- Shell layout: header, sidebar, dark theme.
- Routes: `/login`, `/register`, `/dashboard`, `/books`, `/books/new`, `/books/:id`.
- Create-book wizard: a stepper covering Title/Subtitle/Genre/Audience → Tone/Style/Variables → Input content → Confirm.
- POST `/api/books` and `/api/book-requests` endpoints.
- Submitting the wizard inserts `BookProject`, `BookRequest`, and one `Pending` `AIJobQueue` row (job type = PlanBook).
- Seed data: one company, one user, one author.

**Out of scope:**

- The worker. The job row sits Pending.
- Exports, covers, marketing.
- Admin pages.
- File uploads (text input only).

**Done when:** Anton can register, log in, create a book via the wizard, see it in his book list, and confirm the `AIJobQueue` row exists in the DB. The app looks dark and futuristic enough to be motivating, even if individual screens are still rough.

**Estimate:** 3–4 focused sessions.

**Pre-flight (resolved 2026-05-20):** DB = `madapi` on remote SQL Server, creds in `.env` (which will be added to `.gitignore` as the first commit, with a committed `.env.example` placeholder). Storage = local filesystem. Auth = JWT + ASP.NET Identity. Email = DreamHost SMTP via MailKit.

## Phase 2 — Worker poking holes: first end-to-end

**Goal:** A `Pending` job actually gets picked up, processed, and the user sees a result. One chapter, one agent, real text.

**Scope:**

- `workers/claude-desktop/` set up with `PROMPT.md`, `claim-job.sql`, `update-progress.sql`, `heartbeat.sql`.
- `madauthor_worker` SQL login with limited grants.
- Schedule entry via `/schedule` skill (every 60s) targeting the worker prompt.
- One agent prompt template: `packages/prompts/planner.md` (the Planner).
- Worker dispatch for `JobType = PlanBook`: claim → run Planner subagent → write BookChapters rows with `Status = Planned` → mark Completed.
- API: Hangfire recurring job that reads `AIJobQueue` progress every 2s and pushes `JobProgress` events over SignalR.
- Angular: book detail page subscribes to `project:{id}` SignalR group, shows a progress bar and the planned chapters as they appear.

**Out of scope:**

- Drafting chapter content (Phase 3).
- Editing, continuity, metadata, cover, marketing (Phase 3+).
- Exports (Phase 4).
- File uploads (Phase 3+).

**Done when:** Anton submits a book via the wizard, watches the planner run live in the UI within ~60s of submission, and sees the chapter outline appear. Worker heartbeat row visible in Admin (or directly in DB) within 60s of starting.

**Estimate:** 3–5 focused sessions. The first end-to-end is always the hardest because every layer has to work.

## Phase 3 — Full agent pipeline + file uploads

**Goal:** A submitted book gets fully drafted, edited, continuity-checked, and metadata-generated. The user sees a complete book they can read in the Preview Reader.

**Scope:**

- All agent prompts: planner, researcher, writer, editor, continuity, publisher, marketer (cover deferred to Phase 5).
- Worker dispatch for every `JobType` (except Cover).
- API: after Planner completes, automatically enqueue `DraftChapter` jobs (one per planned chapter); after each Draft completes, enqueue `EditChapter`; after all Edits, enqueue Continuity; then Publisher; then Marketer.
- Quality gates as described in [04 §5](04-ai-orchestration.md).
- File uploads: two-step signed-URL flow; PDF/DOCX/TXT extracted to plain text and stored as research input.
- Preview Reader: renders `BookChapters.ContentMarkdown` with chapter navigation, font controls, and a "regenerate this chapter" button.
- Notifications: in-app + email when a project completes the pipeline.

**Out of scope:**

- Exports (Phase 4).
- Cover generation (Phase 5).
- Voice-to-text + OCR (Phase 6).
- Translation (Phase 6+).

**Done when:** Anton submits an idea, files, and notes; comes back later (or watches live); sees a full draft of a book ready to read in the Preview Reader; gets an email saying it's done.

**Estimate:** 5–8 focused sessions. The longest phase; this is where the value of the product becomes real.

## Phase 4 — Exports

**Goal:** A completed book can be downloaded as PDF, EPUB, and DOCX.

**Scope:**

- Hangfire job: `RenderPdfExport` using QuestPDF. Includes title page, copyright page, ISBN page, TOC with page numbers, headers/footers, chapter title styling.
- Hangfire job: `RenderEpubExport` — XHTML chapters, `content.opf`, `toc.ncx`, zipped to `.epub`, validated with `epubcheck` if installed.
- Hangfire job: `RenderDocxExport` using DocumentFormat.OpenXml.
- API: `POST /api/projects/{id}/exports` — body specifies which formats. Inserts `BookExports` rows with `Status = Queued`.
- Hangfire picks Queued rows, renders, uploads to blob storage, sets `Status = Ready`, fires notification.
- Signed download URL endpoint with 5-min TTL.
- Frontend: Exports page lists ready/in-progress exports, generates download links on demand.

**Out of scope:**

- KDP/IngramSpark print-ready PDFs with bleed and spine (Phase 5).
- HTML and Markdown exports — trivial, lump in at the end.

**Done when:** Anton clicks "Export as PDF" and downloads a readable, properly formatted PDF; same for EPUB and DOCX.

**Estimate:** 4–6 focused sessions. Each format has surprises.

## Phase 5 — Print-ready + covers

**Goal:** Books can be sent to a print-on-demand service (KDP first). Covers can be generated by AI.

**Scope:**

- Print-ready PDF profiles for KDP and IngramSpark: trim size, bleed, spine width calculation from page count and paper weight, ISBN/barcode placement, CMYK color profile if the chosen PDF library supports it.
- A KDP cover wrap PDF (front + spine + back as a single PDF at the right dimensions).
- Cover generation: pick a provider (open question in [01 §10](01-architecture.md)). Wire the Cover agent to actually call it. Without picking a provider, the agent can only emit a prompt; the user uploads a cover.
- Frontend: Publishing tab shows print-ready exports separately, with profiles per platform.

**Out of scope:**

- Direct submission to KDP via their API. (Recommend Anton uploads manually from KDP's site — their API is restricted access.)
- Hardcover and coil-binding presets — easy to add once paperback is done.

**Done when:** Anton can download a KDP-ready interior PDF and cover wrap PDF and successfully upload them to KDP's dashboard without their preview tool flagging issues.

**Estimate:** 4–6 focused sessions.

## Phase 6 — Polish + admin + the rest

**Goal:** The platform is usable by people other than Anton.

**Scope (cherry-pick what matters):**

- Admin dashboard: queue health, worker heartbeats, failed jobs with re-run, AI usage graphs.
- Voice-to-text ingestion (Azure Speech or Whisper).
- OCR for image uploads (Azure Document Intelligence or Tesseract).
- Translation pipeline (multi-language project support).
- Audio version generation (TTS, third-party).
- Course/workbook variants.
- Kids version, teacher guide, presentation slides — these are Marketer-agent-adjacent variants.
- Collaboration: invite users to a book project.
- Billing if going commercial.
- Beautiful empty states, skeleton loaders, microinteractions.

**Out of scope:**

- Anything not on Anton's actual workflow path. Don't build for hypothetical users.

**Done when:** Anton declares it ready.

**Estimate:** Ongoing.

## Sequencing logic

The phases compound on each other:

- **Phase 1 first** because without auth + create-book + the DB schema, nothing else can be built.
- **Phase 2 before Phase 3** because the worker plumbing has to exist before the full pipeline can flow through it.
- **Phase 3 before Phase 4** because exports need finished content to render.
- **Phase 4 before Phase 5** because basic PDF/EPUB is the foundation of print-ready PDF.
- **Phase 5 and 6 are interchangeable** depending on what Anton needs first.

## How big is this, really

Adding up estimates: 19–29 focused sessions to reach a "Anton can produce a publishable book" milestone. Spread over realistic work cadence (a couple of sessions per week), that's 2–4 months of part-time work.

Faster than that requires:

- Skipping Phase 6 entirely until needed (likely).
- Letting the UI be functional-but-rough until Phase 5 is done.
- Picking quick provider options (Azure Blob over S3, ASP.NET Identity over external auth) to avoid setup overhead.

I will push back if a session's scope risks producing shallow scaffolding rather than working code. That's the [feedback_docs_before_code](C:\Users\xy26114\.claude\projects\C--Code-madauthor\memory\feedback_docs_before_code.md) rule applied at the session level.

## Definition of "done" for Phase 0

Anton reads the five docs in `docs/`, asks for changes if any decision feels wrong, and confirms the design. Then we move into Phase 1.
