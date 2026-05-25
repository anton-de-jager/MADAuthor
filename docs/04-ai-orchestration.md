# 04 - AI orchestration

This document specifies how the worker (Claude Code Desktop, see [03](03-worker-and-job-lifecycle.md)) decomposes a book-generation request into a multi-stage agentic pipeline, what each agent does, and how prompts and variables are managed.

## 1. The pipeline

A `BookRequest` becomes a directed sequence of `AIJobQueue` entries. The pipeline:

```
   ┌─────────────┐
   │  Intake     │   API writes BookRequest + initial BookProject + first AIJobQueue row
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │  PlanBook   │   Planner agent → BookProjects.WorkflowStage=Planning, generates
   │             │   chapter outline (creates BookChapters rows with Status=Planned)
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │ ResearchTopic│  Researcher agent (optional, depends on Variables.nonfiction.researchDepth)
   │  (per topic)│   → writes research notes to a Research table (or BookAssets as JSON)
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │ DraftChapter│   Writer agent, one job per chapter, processed in order
   │  (per chap) │   → BookChapters.ContentMarkdown filled, Status=Drafted
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │ EditChapter │   Editor agent, one job per chapter
   │  (per chap) │   → updates ContentMarkdown, Status=Editing then Final
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │ Continuity  │   Continuity agent reads ALL final chapters together
   │   Check     │   → flags inconsistencies, may emit follow-up Edit jobs
   └──────┬──────┘
          ▼
   ┌──────────────┐
   │  Metadata    │   Publisher agent → BookProjects KDP fields, Description,
   │              │   ISBN page text, copyright text
   └──────┬───────┘
          ▼
   ┌─────────────┐
   │  Cover      │   Cover agent → image prompt, BookCovers row + asset
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │  Marketing  │   Marketer agent → BookAssets containing launch kit
   └──────┬──────┘
          ▼
   ┌─────────────┐
   │  Exports    │   Worker INSERTs BookExports(Status=Queued) - Hangfire picks up
   └─────────────┘
```

Each box is a separate `AIJobQueue` entry. The worker processes one at a time. Many of these can run in parallel across different `BookProject`s but for a single project they're serialized (see [03 §13](03-worker-and-job-lifecycle.md)).

## 2. Agents

Each agent corresponds to a Claude Code subagent (invoked via the Agent tool) with a dedicated prompt template under `packages/prompts/`.

### 2.1 Planner

**Input:** the full `BookRequest` (raw user content, AI instructions, variables), `BookProject` (title, genre, audience), any uploaded `BookAssets` summarized into text.

**Output:**

- A chapter outline (chapter number, title, summary, target word count).
- Suggested themes and a one-paragraph narrative arc.
- A list of research topics the Researcher should investigate (for non-fiction).
- Suggested characters for fiction → emitted as `BookCharacters` rows.

**Writes:**

- `BookChapters` rows with `Status = Planned`.
- `BookProjects.WorkflowStage = Planning`, `EstimatedWordCount`, `EstimatedPageCount`.
- `BookCharacters` rows.

**Quality gate:** if the outline has fewer than 3 or more than 40 chapters, or estimated word count is wildly off from `TargetWordCount`, the planner re-plans once with the constraint surfaced. After two attempts, the job is marked Failed (terminal) and the user is asked to revise.

### 2.2 Researcher

**Input:** a list of research topics from the Planner, plus the user's uploaded source material (PDFs, notes).

**Output:** a research dossier per topic - facts, statistics, quotes, references, citations in the project's `CitationStyle`.

**Writes:** rows in `BookAssets` with `AssetType = Generated`, MimeType `application/json`, containing structured research notes the Writer can pull from.

**Optional.** Only runs if `Variables.nonfiction.researchDepth >= 3` or `Variables.fiction.worldBuildingDepth >= 4`.

### 2.3 Writer

**Input:** one `BookChapter` row (with `Status = Planned`), the project's variables (especially tone, POV, chapter length, vocabulary, dialogue frequency), and relevant research dossiers.

**Output:** the full Markdown content of one chapter.

**Writes:** `BookChapters.ContentMarkdown`, `WordCount`, `Status = Drafted`.

**Constraints:**

- Word count: target ±20% based on `Variables.writing.chapterLength` (short=2000, medium=3500, long=6000 words).
- POV consistency: must match the project's POV.
- Forward references only: a chapter shouldn't reference content from a chapter that hasn't been written yet.

### 2.4 Editor

**Input:** one drafted `BookChapter`, the variables, and the chapters immediately before and after (for context).

**Output:** an edited version of the chapter - grammar, flow, clarity, consistency.

**Writes:** updates `ContentMarkdown` (overwrites the draft), sets `Status = Final`. Optionally writes an `EditNotes` blob to `BookAssets` for later author review.

### 2.5 Continuity

**Input:** ALL final `BookChapters` for the project, plus `BookCharacters`.

**Output:** a continuity report - character name/trait inconsistencies, timeline conflicts, tone drift, plot holes.

**Writes:**

- If issues are found: emits new `EditChapter` jobs targeting the offending chapters with the continuity notes in the input JSON. Recursive - capped at 2 continuity passes.
- A continuity report blob in `BookAssets`.

### 2.6 Publisher (metadata)

**Input:** the completed `BookProject` and its final chapters.

**Output:**

- KDP-ready description (≤4000 chars, formatted with HTML the way KDP accepts).
- 7 keywords for KDP.
- 2–3 BISAC subject codes.
- Suggested categories.
- Subtitle if not set.
- ISBN-page text and copyright text.
- Acknowledgements and dedication scaffolding (user fills in names).
- Author bio (uses `Authors.Biography` as a starting point).

**Writes:** updates `BookProjects.Description`, `CopyrightText`; creates a single `BookAssets` row of type `Generated` with all metadata as structured JSON.

### 2.7 Cover

**Input:** the project's metadata, genre, mood, and the `Variables.publishing.coverStyle`.

**Output:** a detailed image-generation prompt, and (if a cover generation service is wired) the generated image.

**Writes:** `BookCovers` row(s) with `Prompt`, `Style`, `Status`, and the linked `BookAssets.AssetId` once an image exists.

**Open question:** which image provider? Deferred to Phase 5. For Phase 1, the Cover agent only produces the prompt and leaves the user to upload a cover image themselves.

### 2.8 Marketer

**Input:** finalized book + metadata.

**Output:**

- 10–15 social posts (Twitter/X, LinkedIn, Instagram captions) themed by chapter.
- 3 email-campaign drafts (announcement, launch day, post-launch nudge).
- 1 launch checklist (countdown by day).
- 5 ad headline + body variants for paid ads.

**Writes:** a single `BookAssets` row of type `Generated` containing the launch kit as structured JSON.

## 3. Prompt templating

All prompt templates live under `packages/prompts/` as Markdown files. One file per agent, version-controlled, reviewable.

Template structure:

```markdown
---
agent: writer
version: 1
description: Draft a single chapter from a planned outline.
inputs:
  - chapter (BookChapter)
  - project (BookProject)
  - request (BookRequest)
  - research (optional, JSON dossier)
---

# Writer - Draft Chapter

You are the Writer agent for MADAuthor. You write one chapter of a book.

## Project context

Title: {{ project.title }}
Genre: {{ project.genre }}
Audience: {{ project.targetAudience }}
Tone: {{ request.variables.writing.tone }}
POV: {{ request.povStyle }}
Chapter length target: {{ request.variables.writing.chapterLength }}
  ({{ chapterLengthWords }} words ±20%)

## Chapter to write

Chapter {{ chapter.chapterNumber }}: {{ chapter.title }}

Summary from the plan:
{{ chapter.summary }}

## Style variables

- Humor: {{ request.variables.writing.humorLevel }}/5
- Emotional intensity: {{ request.variables.writing.emotionalIntensity }}/5
- Dialogue frequency: {{ request.variables.writing.dialogueFrequency }}/5
- Vocabulary sophistication: {{ request.variables.writing.vocabularySophistication }}/5
- Sentence complexity: {{ request.variables.writing.sentenceComplexity }}/5

{% if research %}
## Research dossier
{{ research.content }}
{% endif %}

## Output format

Return the chapter as Markdown. Use H1 for the chapter title, H2 for section
breaks. No commentary or framing - just the chapter content.

## Constraints

- Do not write content that contradicts earlier chapters (provided below).
- Stay in {{ request.povStyle }}.
- Approximately {{ chapterLengthWords }} words.
```

Rendering: a tiny templating step in the worker fills `{{ … }}` from the runtime context before handing the rendered prompt to the Agent tool. Liquid-style syntax keeps the template engine trivial.

**Versioning:** when a prompt changes, the file's `version` frontmatter bumps. The worker records `PromptVersion` in `AIJobQueue.OutputJson` so any regression in output quality can be traced to a prompt change.

## 4. Variable injection

The `BookRequests.Variables` JSON ([02 §4](02-data-model.md)) is the single source of style direction. Agents read only the variables relevant to them:

| Agent | Variable groups consumed |
| --- | --- |
| Planner | writing, fiction or nonfiction, christian, publishing.trimSize |
| Researcher | nonfiction.researchDepth, nonfiction.citationCount |
| Writer | writing (all), POV from request |
| Editor | writing.simplicityLevel, writing.vocabularySophistication |
| Continuity | (none - operates on text alone) |
| Publisher | project.genre, publishing.kdpOptimization |
| Cover | publishing.coverStyle, project.genre |
| Marketer | project.targetAudience, project.genre |

The variables are merged with sensible defaults at the API layer so an agent can rely on all keys being present.

## 5. Quality gates

Between stages, the worker performs cheap deterministic checks before advancing:

| Gate | Where | What |
| --- | --- | --- |
| Outline sanity | After Planner | Chapter count 3–40; word counts sum to within 30% of target |
| Chapter completeness | After Writer | Markdown parses; word count within ±20% of target; no `TODO`/`TBD` markers |
| Edit didn't shrink content | After Editor | Edited word count ≥ 80% of drafted word count (catches Editor truncating) |
| Continuity loop bound | After Continuity | Max 2 continuity passes before forcing Completed |
| Metadata fields populated | After Publisher | Description ≤4000 chars; ≥1 BISAC code; ≥5 keywords |

Gate failures either re-emit the same job (with a remediation hint) or mark the job Failed and notify the user.

## 6. Token / usage tracking

Because the worker uses Claude Code Desktop's subscription rather than per-call API billing, we don't have token-level tracking from the SDK. We approximate:

- The worker writes a `UsageEvent` row per agent invocation with `Agent`, `JobId`, `StartedAt`, `CompletedAt`, `InputCharCount`, `OutputCharCount`.
- Char counts ≈ tokens × 4, good enough for capacity planning.
- The Admin dashboard graphs these over time.

If we later move to an API-key worker, real token counts replace approximations transparently.

## 7. Determinism vs creativity

The pipeline mixes deterministic and creative work:

| Stage | Determinism preference |
| --- | --- |
| Planner | Medium (creative but reproducible structure helps debugging) |
| Researcher | High (facts shouldn't drift) |
| Writer | Low (creative latitude) |
| Editor | High (we want predictable improvements) |
| Continuity | High |
| Publisher | High (KDP fields are formulaic) |
| Cover | Low (visual creativity) |
| Marketer | Medium |

Each prompt template specifies its temperature preference. The worker honors it when invoking the subagent (where supported).

## 8. Where to inspect output

After a successful generation, the user sees:

- Chapters in the Preview Reader (rendered from `BookChapters.ContentMarkdown`).
- Cover (if generated) in the project's cover gallery.
- Metadata in the Publishing tab.
- Marketing assets under Assets → Marketing.
- Exports (PDF, EPUB, DOCX) appear once Hangfire finishes rendering.

Every stage's input/output JSON is stored in `AIJobQueue.OutputJson` for audit and re-run.

## 9. Orchestrator durability

The `PipelineOrchestrator` (in `apps/api/MadAuthor.Api/Realtime/PipelineOrchestrator.cs`) is a `BackgroundService` hosted inside the API process. It is the **only thing** that enqueues stage-N+1 jobs when stage-N completes - the worker only does the work; it does not chain.

Because it's a hosted service, it only runs when the API runs. A stalled API = a stalled pipeline. To survive restarts and intermittent failure, the orchestrator runs at two cadences:

| Layer | Cadence | What it does |
| --- | --- | --- |
| **Tick** (event-driven) | every 5s | Reads `AIJobQueue` rows with `Status=Completed` and `CompletedDate > _lastSeenTick`, calls `HandleCompleted` for each, advances `_lastSeenTick` to the newest seen. Cursor is in-memory; no persistence. |
| **ReconcileGaps** (state-driven) | every 2min | For every non-terminal `BookProject` with completions in the last 30 days, re-invokes `HandleCompleted` on every completion. Idempotent thanks to `AnyAsync` guards in every handler. Self-heals any miss. |

### Why both

The Tick is fast but fragile: if the API was down when a job completed, that completion is past the cursor when the API comes back and the Tick will never see it. Earlier the cursor initialized to `UtcNow - 10min`, which silently dropped any completion older than 10 minutes at startup.

The reconciler is the durability layer. It scans by **state**, not by timestamp cursor, so it doesn't care when the completion happened - only whether the follow-up that should exist actually exists.

### Idempotency contract

Every handler in `PipelineOrchestrator` MUST be safe to invoke multiple times on the same job. The pattern:

```csharp
var alreadyExists = await db.AIJobQueue.AnyAsync(j =>
    j.BookProjectId == job.BookProjectId
    && j.JobType == followUpType
    && /* uniqueness predicate, usually InputJson chapterId match */, ct);
if (alreadyExists) return;
```

If you add a new follow-up enqueue, add the matching `AnyAsync` guard. Otherwise the reconciler will fire duplicates.

### Startup lookback

`_lastSeenTick` is initialized to `UtcNow.AddHours(-24)` on process start. That's the belt-and-braces: even before the first `ReconcileGaps` fires (~2min in), the Tick re-emits follow-ups for the past day's completions. The 24h window plus the 30-day reconciler window cover essentially all realistic outage scenarios.

### Manual recovery playbook

When a book stalls despite the orchestrator running, walk through these steps:

1. **Identify the stall point.** Query `AIJobQueue` grouped by `JobType` and `Status` for the project. Compare the job count per type against `BookChapters` counts (e.g. 15 `DraftChapter` completed but only 12 `EditChapter` rows → 3 missing edits).
2. **Find the offending chapters.** `SELECT Id, ChapterNumber, Status FROM BookChapters WHERE BookProjectId=… AND Status < 4`. Status 2 = Drafted (needs Edit), 4 = Final.
3. **Enqueue the missing rows manually.** Insert `Status=Pending` rows into `AIJobQueue` with the right `JobType` and `InputJson` shape (see existing rows in the same project for the canonical shape). The worker's next scheduled tick will claim them.
4. **For project-level stages** (`ContinuityCheck`, `GenerateMetadata`, `GenerateMarketing`), `InputJson` can be `NULL`.
5. **If `OnMarketingComplete` didn't fire** (project still not `ReadyForReview` despite Marketing complete), restart the API or wait for the next reconciler tick - it will retroactively flip the project columns and send the owner email.

## 10. Open questions

- **Human-in-the-loop checkpoints.** Confirmed 2026-05-20: `RequireOutlineApproval` is a `BookProjects` column, defaults to `true`. After the Planner job completes, the worker stops and waits; the next job (Researcher/Writer) is only enqueued when the user approves the outline via the API. Users can flip the setting off per project for fully-autonomous runs.
- **Resume semantics.** If a job is mid-pipeline and the user edits the project, does the next stage see the edited version? Recommend yes - agents always re-read from the DB at job start. But also recommend that ongoing jobs are paused (`Status = Cancelled`) when the project is meaningfully edited mid-flight, and the user is shown a "Restart pipeline?" prompt.
- **Multi-language.** The schema has `Language` on `BookProjects` but the prompt templates are English. For Phase 1, only English. For multi-language we either translate prompts (risky - language-specific writing conventions matter) or maintain per-language prompt sets.
- **Sensitive content guardrails.** What categories are blocked outright (illegal content, CSAM, etc.) vs gated (graphic violence in fiction, theology that misrepresents specific traditions)? Needs a content-policy doc before launch.
