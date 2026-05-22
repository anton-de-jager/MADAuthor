# 03 — Worker and job lifecycle

This is the load-bearing piece of MADAuthor's design and the one that diverges most from a conventional SaaS architecture. The AI worker is **Claude Code Desktop itself**, scheduled to wake up periodically, poll the database for pending jobs, and process them as agentic sessions.

This document explains: why we chose this pattern, the job state machine, the claim protocol, the scheduling mechanism, the prompt the scheduled task feeds Claude, how progress flows back to the user, and how failures are handled.

## 1. Why this pattern

**The conventional alternative** would be a long-running .NET worker service (BackgroundService or Hangfire job) that calls the Anthropic or OpenAI HTTP API per chapter. It works, but it has costs Anton wants to avoid for now:

- An API key with billing attached, rate limits, monthly caps.
- Per-token billing on top of his existing Claude subscription.
- An additional process to deploy, monitor, scale.
- Reimplementing the agentic loop (planner → writer → editor) that Claude Code Desktop already runs natively.

**Claude Code Desktop as the worker** gives us:

- Use of the existing Claude subscription instead of API billing.
- A first-class agentic runtime (subagents, tools, planning) without writing it.
- Full local file access, so generated drafts can be inspected and exported without going through the API.
- Zero additional services to deploy in Phase 1.

**The trade-off** is that the worker runs on Anton's desktop, so it's single-instance, only available when his machine is on, and not horizontally scalable until we move to a hosted variant. This is acceptable for Phase 1 because MADAuthor in Phase 1 is for Anton's own use; multi-user comes later.

The whole point of writing the API and worker against a **database contract** (rather than RPC) is that we can swap the worker for a hosted variant later without touching the API.

## 2. Job state machine

A job in `AIJobQueue` moves through these states:

```
                ┌──────────┐
                │ Pending  │  ← API inserts row here when user submits a BookRequest
                └────┬─────┘
                     │ worker reserves via UPDATE … OUTPUT
                     ▼
                ┌──────────┐
                │ Claimed  │  ← worker holds an exclusive lock (LockExpiresAt set)
                └────┬─────┘
                     │ worker starts agentic execution
                     ▼
              ┌──────────────┐
              │ InProgress   │  ← worker writes Stage + Progress periodically
              └──┬───────┬───┘
                 │       │
       success   │       │ exception / retry-eligible error
                 ▼       ▼
          ┌──────────┐  ┌────────────────────┐
          │Completed │  │  Failed (retry)    │  ← RetryCount++, requeued as Pending
          └──────────┘  └────────────────────┘
                                 │
                                 │ RetryCount > MaxRetries
                                 ▼
                         ┌────────────────────┐
                         │  Failed (terminal) │
                         └────────────────────┘

  At any state: user can request cancellation → Cancelled (worker checks each stage).
```

State transitions are written by the worker (claim, progress, completion) and the API (submission, cancellation). No other party writes to `AIJobQueue`.

## 3. Atomic job claim

This is the critical bit of concurrency. The claim must be atomic, so that if two workers ever exist (or one worker tries to claim two jobs in the same tick), they don't pick the same row.

SQL Server makes this easy with `UPDATE … OUTPUT`:

```sql
-- claim-job.sql
DECLARE @WorkerId NVARCHAR(200) = @workerId;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();

;WITH next_job AS (
    SELECT TOP (1) *
    FROM AIJobQueue WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Status = 0  -- Pending
       OR (Status = 1 AND LockExpiresAt < @Now)  -- Claimed but expired
    ORDER BY Priority ASC, CreatedDate ASC
)
UPDATE next_job
SET Status         = 1,              -- Claimed
    ClaimedBy      = @WorkerId,
    ClaimedAt      = @Now,
    LockExpiresAt  = DATEADD(MINUTE, 15, @Now),
    UpdatedDate    = @Now
OUTPUT
    INSERTED.Id,
    INSERTED.BookProjectId,
    INSERTED.BookRequestId,
    INSERTED.JobType,
    INSERTED.InputJson,
    INSERTED.RetryCount;
```

Key points:

- `UPDLOCK, READPAST` together mean "lock the row I'm reading, and skip rows other transactions have already locked." This is the standard SQL Server FIFO-queue pattern.
- The lock is held only for the duration of the `UPDATE`, not the duration of the job. The `LockExpiresAt` column extends the logical lock so other workers know not to touch the row.
- A worker that crashes or hangs leaves `LockExpiresAt` in the past after 15 minutes. The next polling cycle reclaims the row (RetryCount stays the same — this is a "lease expired" recovery, not a retry).

## 4. Scheduling Claude Code Desktop

Claude Code has a built-in `CronCreate` mechanism for scheduled tasks. We use it to wake a Claude session on a regular cadence.

Setup, run once by Anton (this becomes a script under `workers/claude-desktop/`):

1. Open Claude Code in `C:\Code\madauthor\workers\claude-desktop\`.
2. Run `/schedule` (the schedule skill) to create a cron entry.
3. Schedule: every 60 seconds during working hours (e.g. `*/1 8-22 * * *`) or every 30 seconds (`*/1 * * * *` with intra-minute sleeps) — see §6 for cadence rationale.
4. Prompt: a literal pointer to `workers/claude-desktop/PROMPT.md`.

When the schedule fires, Claude Code wakes, reads `PROMPT.md`, runs the workflow described there, exits.

**Idle behavior.** If no jobs are pending, the prompt instructs Claude to upsert a heartbeat row and exit immediately. Cheap and fast — no LLM tokens consumed beyond reading the prompt and running the SQL.

## 5. The standing worker prompt (`PROMPT.md`)

This is the prompt the scheduled task feeds Claude every wake-up. The actual file will be more detailed; this is the structural skeleton.

```markdown
# MADAuthor Worker — Standing Instructions

You are the MADAuthor AI worker. Each time you wake, you do this loop:

## Step 1 — Heartbeat

Execute `workers/claude-desktop/heartbeat.sql` to upsert this worker's row in
WorkerHeartbeats. Pass `$env:COMPUTERNAME-$PID` as @workerId.

## Step 2 — Claim a job

Execute `workers/claude-desktop/claim-job.sql`. If no row is returned, EXIT
SILENTLY — there is nothing to do. Do NOT continue or speculate.

If a row is returned, you now own job @jobId with type @jobType and input
@inputJson until LockExpiresAt.

## Step 3 — Dispatch

Look up @jobType. Use the Agent tool with the matching subagent and prompt
template from `packages/prompts/`:

| JobType            | Subagent       | Prompt template          |
|--------------------|----------------|--------------------------|
| PlanBook           | planner        | packages/prompts/planner.md |
| ResearchTopic      | researcher     | packages/prompts/researcher.md |
| DraftChapter       | writer         | packages/prompts/writer.md |
| EditChapter        | editor         | packages/prompts/editor.md |
| ContinuityCheck    | continuity     | packages/prompts/continuity.md |
| GenerateCover      | cover          | packages/prompts/cover.md |
| GenerateMetadata   | publisher      | packages/prompts/publisher.md |
| GenerateMarketing  | marketer       | packages/prompts/marketer.md |

Hand the subagent: the BookProject + BookRequest context (read from DB), the
prompt template, and the job input JSON.

## Step 4 — Persist results

As the subagent produces output:
- Chapters → INSERT/UPDATE BookChapters
- Plans → write to BookProjects.WorkflowStage and a structured BookRequests update
- Assets → upload to blob storage, INSERT BookAssets
- Metadata → UPDATE BookProjects (description, KDP fields)

Use `workers/claude-desktop/update-progress.sql` between subagent calls to
write Stage and Progress to AIJobQueue.

## Step 5 — Complete or fail

On success:
- UPDATE AIJobQueue SET Status = 3 (Completed), CompletedDate = SYSUTCDATETIME(),
  OutputJson = ...
- If the job spec says "enqueue exports on completion," INSERT BookExports rows
  with Status = Queued so Hangfire picks them up.

On failure:
- If retryable and RetryCount < MaxRetries: UPDATE Status = 0 (Pending),
  RetryCount = RetryCount + 1, ClaimedBy/ClaimedAt/LockExpiresAt = NULL,
  ErrorMessage = <message>.
- Otherwise: UPDATE Status = 4 (Failed), ErrorMessage = <message>.

## Step 6 — Exit

Do not look for another job in the same wake-up. The next schedule tick picks
up the next one. Keeping each wake-up to one job bounds the runtime and
simplifies reasoning.
```

The prompt commits Claude to a tight loop with side-effects only on the DB and blob storage. It is not exploratory work.

## 6. Polling cadence and the cache-window question

Claude Code's prompt cache has a 5-minute TTL. Sleeping past 300 seconds means the next wake-up reads the standing prompt uncached.

Options:

- **Every 30s during active hours** — fast pickup, cheap because the standing prompt is small and stays cached.
- **Every 60s always** — slower pickup, simpler.
- **Every 5 minutes** — cache miss every wake-up, very slow pickup. Don't.

Recommendation: **30s polling** during 8am–10pm local, **5 minute polling** overnight. The schedule skill supports cron expressions, so this is one entry per band.

If the queue is empty most of the time, the wake-up cost is essentially the SQL roundtrip + a heartbeat upsert. The agent never enters a generation loop on an empty queue.

## 7. Progress reporting → SignalR

The worker writes progress to `AIJobQueue.Stage` (free-form text) and `AIJobQueue.Progress` (0–100). It does **not** talk to the API or SignalR directly.

How progress reaches the frontend:

1. Worker writes `Stage = "writing-chapter-3"`, `Progress = 42`.
2. A Hangfire recurring job in the API process runs every 2s: it selects from `AIJobQueue` where `UpdatedDate > @lastTick AND Status IN (1, 2)` (Claimed, InProgress).
3. For each row it pushes a `JobProgress` event to the SignalR group `project:{BookProjectId}`.
4. Subscribed clients see the progress bar update.

The 2s latency is fine for a generation that takes minutes per chapter. If lower latency is ever needed, switch the recurring job to a `BackgroundService` doing `WaitForChangesAsync` over a SQL Service Broker or polling at 200ms — not necessary for Phase 1.

## 8. Failure handling

Failure categories:

| Failure | Action |
| --- | --- |
| Transient (network blip, blob upload timeout) | Retry within the same job; if exhausted, mark Pending with RetryCount+1 |
| Worker crash / hang | LockExpiresAt passes; next worker reclaims; RetryCount unchanged (lease expiry, not a retry) |
| Bad input (invalid JSON, missing FK) | Mark Failed terminal immediately. No retry. Notify user. |
| Subagent refuses or produces empty output | Mark Failed retryable. Bump RetryCount. |
| MaxRetries exceeded | Mark Failed terminal. INSERT a `Notification` for the user. |

`MaxRetries` defaults to 3, configurable per `JobType` (Cover generation might be 5 — provider flakiness).

## 9. Cancellation

The API exposes `POST /api/jobs/{jobId}/cancel`. It sets `Status = 5 (Cancelled)`.

The worker checks for `Status = 5` at every stage transition (before each subagent call). If cancelled, it exits its loop, writes a final progress entry, and releases the lock. Mid-stage cancellation is best-effort — Claude can't be interrupted mid-token-generation cleanly, but the next stage gate stops the work.

## 10. Single-worker constraint and future scaling

For Phase 1, only one worker exists (Anton's desktop). The locking protocol is forward-compatible: when multiple workers are added, no code changes are required.

When scaling out:

- **Hosted variant of Claude Code Desktop** — if Anthropic provides this in the future, schedule it the same way on a cloud VM.
- **API-key worker fallback** — a .NET BackgroundService implementing the same `IJobExecutor` interface, calling the Anthropic SDK. Useful for high-volume workloads.
- **Manual override** — Anton can manually claim and process a job from a future Admin UI for difficult cases.

All three variants present the same DB contract.

## 11. What the worker does NOT do

To keep the worker's responsibilities clear:

- **Does not render PDF/EPUB/DOCX.** That's deterministic Hangfire work. The worker writes chapters; Hangfire renders.
- **Does not send notifications.** The API/Hangfire sends. The worker just enqueues the work by inserting a row.
- **Does not authenticate.** It reads the DB with a dedicated `madauthor_worker` SQL login that has limited grants (SELECT on most tables, INSERT/UPDATE on `AIJobQueue`, `BookChapters`, `BookAssets`, `BookExports`, `WorkerHeartbeats`, `BookCovers`).
- **Does not talk to the API.** Strict isolation.

## 12. Setup checklist (Phase 1)

When we're ready to wire this up:

1. EF Core migration creates `AIJobQueue`, `WorkerHeartbeats`, etc.
2. Add a `madauthor_worker` SQL login with limited grants.
3. Create `workers/claude-desktop/PROMPT.md`, `claim-job.sql`, `heartbeat.sql`, `update-progress.sql`.
4. Test manually: insert a dummy `Pending` row, run the prompt in Claude Code by hand, verify it claims and processes.
5. Use the schedule skill to create the cron entry.
6. Watch `WorkerHeartbeats` for a fresh ping; watch `AIJobQueue.Status` transitions for a real job.

## 13. Open questions

- **Worker observability** — should the worker emit structured logs back to the DB (`WorkerLogs` table) for the Admin UI to read, or just rely on Claude Code's local transcript? Recommend a thin `WorkerLogs` table with INFO/WARN/ERROR rows so the Admin UI has something to render.
- **Concurrency cap per project** — if many jobs queue for one book, do we want to serialize them so chapters complete in order? Recommend YES — claim filter excludes jobs whose `BookProjectId` already has an `InProgress` job, unless the JobType is independent (Cover, Marketing).
- **Cold-start cache strategy** — first wake-up of the morning incurs a cache miss. Acceptable. Don't over-engineer.
