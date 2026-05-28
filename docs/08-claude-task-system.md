# 08 - Operator task system (`/claude`)

A second autonomous pipeline alongside the book-generation pipeline. Where `AIJobQueue` handles book-shaped jobs (plan, draft, edit chapters), the new **ClaudeTask** queue handles **dev / operator tasks** ("fix this bug", "add this feature", "audit this prompt"). A super-admin operator page surfaces them. A polling worker drains them. A codebase scanner files new ones.

This doc is a MADAuthor-native translation of an external template spec authored for a NestJS / Prisma / MSSQL stack. The shape of the design is preserved; everything stack-specific has been remapped to MADAuthor's actual idioms (.NET 8 / EF Core / SQL Server / Angular standalone + Tailwind `ink-*` primitives / SignalR / `claude.exe` invoked from PowerShell).

## 1. Why a second pipeline (and not extend `AIJobQueue`)

Anton's call (decision recorded in session 2026-05-20): both stay side by side. The two pipelines do genuinely different things:

| | `AIJobQueue` (existing) | `ClaudeTasks` (new) |
| --- | --- | --- |
| **Domain** | One book → many job types (`PlanBook`, `DraftChapter`, `EditChapter`, etc.) | Free-form dev tasks ("fix the cover-url bug", "audit the planner prompt") |
| **Driver** | `BookProject` → `BookRequest` → enqueue | Operator types into `/admin/claude` or scanner posts findings |
| **Shape** | Structured `JobType` enum + `InputJson` per type | Plain `title` + `description` + `notes` |
| **Worker** | `MadAuthor.Worker.exe` CLI + Claude Code Desktop session (CronCreate-scheduled today) | `claude.exe --print` invoked via PowerShell on a Windows Task Scheduler trigger |
| **Status state machine** | `Pending → Claimed → InProgress → Succeeded / Failed`, retryable | `PENDING → IN_PROGRESS → COMPLETED / FAILED / CANCELLED / DEFERRED`, **`FAILED` is terminal** |
| **Audience** | All authenticated users (own their books) | Super-admins only (operator role) |

Sharing one table would force the structured job types and the free-form dev tasks into the same schema. Two tables, one operator page each, much cleaner.

## 2. Schema (EF Core)

New entities in `MadAuthor.Domain`:

```csharp
public enum ClaudeTaskStatus : byte {
    Pending = 0, InProgress = 1, ToBeDeployed = 2,
    Completed = 3, Cancelled = 4, Failed = 5, Deferred = 6,
}

public class ClaudeTask : IAuditableEntity {
    public int Id { get; set; }              // autoincrement int (not Guid) for short IDs in the UI
    public string Title { get; set; } = "";  // [MaxLength(300)]
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public ClaudeTaskStatus Status { get; set; } = ClaudeTaskStatus.Pending;
    public byte Priority { get; set; } = 3;  // 1=critical, 2=high, 3=normal, 4=low
    public string? AttachmentsJson { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

public class ClaudePromptTemplate : IAuditableEntity {
    public int Id { get; set; }
    public string Name { get; set; } = "";   // unique, [MaxLength(200)]
    public string? Description { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

public class AppSetting {                    // shared with future generic settings
    public string Key { get; set; } = "";    // PK
    public string ValueJson { get; set; } = "";
    public DateTime UpdatedDate { get; set; }
}
```

Index on `(Status, Priority, Id)` for the `next()` query. One EF migration. Auto-applied on API startup per the existing `Database.MigrateAsync()` hook in `Program.cs`.

Seed `AppSetting` rows: `workerActive=true`, `scannerActive=true`, `deployNext=false`.

## 3. API endpoints

Under `apps/api/MadAuthor.Api/Controllers/ClaudeTasksController.cs`:

| Verb + path | Body / query | Returns | Auth |
| --- | --- | --- | --- |
| `GET    /api/claude-tasks` | `?statusFilter=&limit=` | bucketed list (active / to-deploy / terminal) | `[Authorize(Roles="Admin,Owner")]` |
| `GET    /api/claude-tasks/next` | - | 200 `{task}` or 204 | worker-token OR admin |
| `GET    /api/claude-tasks/{id}` | - | task | admin |
| `POST   /api/claude-tasks` | `CreateClaudeTaskRequest` | task | admin |
| `PATCH  /api/claude-tasks/{id}` | partial update | task | worker-token OR admin |
| `DELETE /api/claude-tasks/{id}` | - | 204 | admin |
| `POST   /api/claude-tasks/import-bulk` | `{items:[...]}` | `{created, skipped}` | admin |
| `POST   /api/claude-tasks/{id}/attachments` | multipart, ≤50MB total | `{file metadata}` | admin |
| `DELETE /api/claude-tasks/{id}/attachments/{name}` | - | 204 | admin |
| CRUD on `/api/claude-prompt-templates` | - | - | admin |
| `GET / PATCH /api/settings` | - | - | admin |

Realtime delivery uses **SignalR** (existing `NotificationHub`) - not SSE. The spec's SSE design was for an app without SignalR; we already have it wired and auth via JWT-from-query is already handled by my earlier `OnMessageReceived` hook. New event group: `claude-tasks` (all admins join). Broadcast `{type:'task.created'|'updated'|'deleted', taskId, task}` on every change.

Status transitions validated server-side. Terminal statuses (`Completed`, `Cancelled`, `Failed`) reject changes without `?override=true`. The state machine table from the spec applies verbatim - only the C# enum names differ.

## 4. Auth - worker-token bypass

The current `[Authorize]` chain takes JWT bearer only. Add a small middleware **before** `UseAuthentication`:

```csharp
app.Use(async (ctx, next) => {
    var presented = ctx.Request.Headers["X-Worker-Token"].ToString();
    var expected  = builder.Configuration["CLAUDE_WORKER_TOKEN"];
    if (!string.IsNullOrEmpty(presented) && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(
              Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected))) {
        // Forge a worker identity so [Authorize] passes for the matching endpoints.
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("worker", "claude-task-worker"),
        }, "WorkerToken"));
    }
    await next();
});
```

The token is generated fresh per environment (32-byte hex). **Do NOT reuse the token shown in the template spec** - that was MADLeads' production token. Store in `.env` as `CLAUDE_WORKER_TOKEN`. Rotation: regenerate, update `.env`, redeploy API, update both worker scripts.

Operator routes use `[Authorize(Roles="Admin,Owner")]`. The first registered user gets these roles automatically (existing `AuthController.Register` behaviour). No new `isSuperAdmin` column needed.

## 5. Operator page (`/admin/claude`)

Standalone Angular component at `apps/web/src/app/features/admin/claude/claude.page.ts`. Lives inside the existing shell layout. Route guarded by a new `adminGuard` (admin/owner role check) - same pattern as the existing `authGuard`.

Uses existing primitives: `bg-ink-900/60`, `border-ink-700`, `bg-brand-600`, `text-brand-200` etc. No `mc-*` primitives - those don't exist in this codebase.

Layout per the spec:
- Header bar with title + icon-only action buttons (Templates, Import, New, Refresh).
- Worker toggle row (Active / Scanner / Deploy-Next switches → PATCH `/api/settings`).
- 6 summary cards (Total / In progress / Pending / To-deploy / Completed / Failed).
- Status filter + "Show last N completed" filter (default: `not_completed`).
- Vertically stacked task rows with status icon, `#id` chip, title, description preview, notes, badges, "Updated X ago", hover-revealed delete.
- Edit / New / Import / Templates modals (all `max-w-xl` or `max-w-2xl`).
- Realtime: subscribe to SignalR `ClaudeTaskEvent` on init; same `NotificationsService.jobProgress$` style. Show milestone toasts via existing `ToastService`.
- Confirm-delete via existing `NotificationService` (no `window.confirm()`).

Sidebar entry "Claude Tasks" added to shell, **only visible** when `currentUser.roles.includes('Admin')`.

## 6. Worker - `.claude/worker/`

PowerShell script + prompt file + state JSON, fired by Windows Task Scheduler every 1 minute. Adaptive backoff ladder unchanged from the spec:

| Streak (consecutive empty polls) | Min seconds between actual polls |
| --- | --- |
| 0 (just had work) | 60 |
| 1–4 | 300 |
| 5–9 | 600 |
| 10–14 | 1800 |
| 15+ | 3600 |

`.claude/worker/worker-iteration.ps1` reads state, decides skip-or-fire, on fire hits `GET /api/claude-tasks/next`, on 200 invokes `claude.exe --print --dangerously-skip-permissions --add-dir C:\Code\madauthor` with the prompt piped via stdin.

`.claude/worker/worker-prompt.md` contains the full agent instructions: pre-flight (`git status` / `git pull --ff-only`), drain loop, batch-sniff for up to 3 additional independent tasks, parallel `Agent` calls in one message, post-batch sanity sweep, deploy at end of iteration, hard rules (no `--no-verify`, no `--force` push, no amends, no mid-queue deploys).

**Critical** - every PowerShell file ASCII-only. PowerShell 5.1 reads UTF-8-no-BOM as cp1252 and mojibakes em-dashes / smart quotes. Already a lesson learned this session (`deploy.ps1` hit this).

State at `.claude/worker/state.json` (gitignored). Log at `.claude/worker/worker.log` (gitignored). Survives reboots via Task Scheduler persistent registration.

## 7. Scanner - `.claude/scanner/`

Fixed hourly cadence - no adaptive logic. Read-only. Reads the repo, finds STUB / BUG / GAP / TODO / DEBT signals, dedupes against the active queue, POSTs up to 20 findings per scan via `import-bulk`. Never modifies source.

Same script/prompt-file split as the worker.

## 8. Independence rules for parallel agents

The spec's batching rule applies cleanly: claim up to 3 additional PENDING tasks that share none of these with task A:
- **Files** - no overlap between the file sets each task would edit.
- **Dependencies** - both editing `MadAuthorDbContext.cs` (or any migration) → NOT independent.
- **Ordering** - task descriptions imply "first do X, then this" → NOT independent.
- **Shape** - code-fix batches with code-fix; site-generation always solo.

If no clean batch can be formed, work A solo. Better one safe task than four with merge conflicts.

## 9. Coexistence with the book pipeline

Both pipelines run from this same Claude Code Desktop machine:

- **Book pipeline** keeps running on the existing CronCreate-scheduled wake-ups (or migrate to Task Scheduler later - see open question #1).
- **Claude task pipeline** runs on its own Task Scheduler entry (`MADAuthorClaudeWorker`), independent of any open Claude Code Desktop window.

The two never claim each other's queue. The book worker only touches `AIJobQueue`; the claude worker only touches `ClaudeTasks`. No conflict.

The existing `claim-batch 6` wave-based concurrency on the book side stays as-is.

## 10. Open questions

1. **Migrate the book pipeline to Task Scheduler too?** Currently CronCreate dies when this Claude Code Desktop window closes. Task Scheduler is more durable. Recommended yes - but separate scope from this doc.
2. **Auto-deploy at end of worker iteration?** Spec says yes, via `pwsh deploy.ps1`. My concern: a bad agent run could push broken code. Suggest gating by a `deployNext` AppSetting (off by default), which the operator flips on for batches they trust.
3. **Attachments - same `IFileStorage` as book attachments?** Yes recommended - re-use `LocalFileStorage`, separate container key (`claude-task-attachments/`).
4. **Scanner scope** - initial pass only scans `apps/api` and `apps/web`. Skip `node_modules`, `bin`, `obj`, `.deploy`, `dist`, migrations. Anything else off-limits?

## 11. Implementation phases (after sign-off)

1. **Schema + migration** - `ClaudeTask`, `ClaudePromptTemplate`, `AppSetting`. Seed defaults.
2. **API controllers** - `ClaudeTasksController`, `ClaudePromptTemplatesController`, `SettingsController`. Worker-token middleware. State-machine validator. SignalR broadcast hooks.
3. **Operator page** - `/admin/claude` route + guard + sidebar entry. Edit / Import / Templates modals.
4. **Worker** - `.claude/worker/` script + prompt + state. Task Scheduler entry.
5. **Scanner** - `.claude/scanner/` script + prompt + state. Task Scheduler entry.
6. **Tests** - state-machine illegal transitions, dedupe on `import-bulk`, worker-token timing-safe compare.
7. **End-to-end verification** - create → status flips → realtime UI → bulk dedupe → manual worker fire → manual scanner fire → delete.

## 12. Estimate

- Phases 1–2 (schema + API): ~1 focused session.
- Phase 3 (operator page): ~2 focused sessions.
- Phases 4–5 (worker + scanner scripts + prompts): ~1 session.
- Phase 6–7 (tests + verification): ~half a session.

**Total: ~5 focused sessions.** Comparable to the upload + wave-batching + HumanVoice arc you just lived through.

## 13. Out of scope

- Multi-machine workers (the spec's Task Scheduler is single-machine).
- Workflow / dependency graphs between tasks (just a flat queue).
- Approval flows / multi-step reviews.
- Reading from external issue trackers (GitHub Issues, etc.) - manual paste / scanner only.
