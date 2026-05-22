# `/claude` Operator Task System — Runtime Setup

End-to-end install and verification steps for the autonomous task system added by
`docs/08-claude-task-system.md`. After completing this checklist the system runs
on its own: the operator queues tasks at `/admin/claude`, the worker drains them
every minute, and the scanner files new findings hourly.

## 1. Generate the worker token

```powershell
$token = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower()
$token  # print and copy
```

Add to `.env` (gitignored, repo root):

```
CLAUDE_WORKER_TOKEN=<the-generated-value>
```

Replace `<PLACE_TOKEN_HERE>` in **three** files:

- `.claude/worker/worker-iteration.ps1` (`$token = '...'`)
- `.claude/worker/worker-prompt.md` (`# Identity` block)
- `.claude/scanner/scanner-prompt.md` (`# Identity` block)

## 2. Run database migrations

The API auto-applies pending migrations on startup. To apply manually:

```powershell
cd apps\api
dotnet ef database update --project MadAuthor.Infrastructure --startup-project MadAuthor.Api
```

The new migration `20260521100343_AddClaudeTaskSystem` creates three tables:
`ClaudeTasks`, `ClaudePromptTemplates`, `AppSettings` and one composite index
`IX_ClaudeTasks_Status_Priority_Id`. The seeder inserts three default rows in
`AppSettings`: `workerActive=true`, `scannerActive=true`, `deployNext=false`.

## 3. Register the schedulers

```powershell
.\.claude\register-schedulers.ps1
```

Creates two Windows Task Scheduler entries:

| Name | Cadence | Purpose |
|---|---|---|
| `MADAuthorClaudeWorker` | every 1 min (adaptive backoff) | Drains the queue, claims independent task batches, spawns up to 4 parallel agents per iteration |
| `MADAuthorClaudeScanner` | every 1 hour | Read-only repo scan; posts new findings to `/api/claude-tasks/import-bulk` |

Verify:

```powershell
Get-ScheduledTask -TaskName "MADAuthorClaude*"
```

## 4. Verify the API

Start the API (if not already running):

```powershell
cd apps\api
dotnet run --project MadAuthor.Api
```

Curl probes (replace `<token>` with your CLAUDE_WORKER_TOKEN):

```powershell
# 1. Worker can poll /next with token bypass (should return 204 on empty queue)
curl -i -H "X-Worker-Token: <token>" http://localhost:5150/api/claude-tasks/next

# 2. List endpoint requires admin bearer (will 401 without token)
curl -i http://localhost:5150/api/claude-tasks

# 3. Settings endpoint returns the three seeded defaults
curl -i -H "X-Worker-Token: <token>" http://localhost:5150/api/settings
```

## 5. Verify the frontend

```powershell
cd apps\web
npm start
```

Open `http://localhost:4200`. Log in with an Admin or Owner account
(the first registered user gets both roles automatically). The sidebar
should show a "Claude Tasks" entry below "Worker queue". Click it to
land on `/admin/claude`.

Create a probe task; verify it appears in the active bucket. PATCH it
to `InProgress`; the badge colour should flip live (real-time SignalR).
PATCH it to `Failed`; the row should move to the terminal bucket and
`GET /api/claude-tasks/next` should NOT return it.

## 6. Manual worker fire

```powershell
Start-ScheduledTask -TaskName "MADAuthorClaudeWorker"
Get-Content -Wait .\.claude\worker\worker.log
```

You should see entries like:

```
2026-05-21T13:00:00Z  FIRE  streak=0 bucket=60s elapsed=… -- checking queue
2026-05-21T13:00:01Z  EMPTY queue (204). streak 0 -> 1. Next bucket: 300s.
```

…or, if a PENDING task exists:

```
2026-05-21T13:00:01Z  WORK  queue non-empty. Invoking Claude Code worker session...
  <claude.exe stdout streamed here>
2026-05-21T13:05:23Z  DONE  claude exit=0 elapsed=322s
```

## 7. Manual scanner fire

```powershell
Start-ScheduledTask -TaskName "MADAuthorClaudeScanner"
Get-Content -Wait .\.claude\scanner\scanner.log
```

Expect a `Scanned N findings. Submitted K, server created C, skipped S.` summary
line. Any new tasks will appear in the operator UI in real time.

## 8. Stop everything

```powershell
Disable-ScheduledTask -TaskName "MADAuthorClaude*"
```

Re-enable with `Enable-ScheduledTask` once you're ready to resume.

To remove entirely:

```powershell
Unregister-ScheduledTask -TaskName "MADAuthorClaude*" -Confirm:$false
```

## Logs + state

| Path | Purpose |
|---|---|
| `.claude/worker/worker.log` | Worker heartbeat + claude.exe stdout/stderr |
| `.claude/worker/state.json` | `{streak, lastFiredAt}` — drives adaptive backoff |
| `.claude/scanner/scanner.log` | Scanner heartbeat + claude.exe stdout/stderr |
| `.claude/scanner/state.json` | `{lastRanAt}` |

All four are gitignored — they're runtime state.

## Token rotation

If the worker token leaks or you want fresh credentials:

1. Generate a new 32-byte hex token.
2. Disable the schedulers: `Disable-ScheduledTask -TaskName "MADAuthorClaude*"`.
3. Update `.env` (`CLAUDE_WORKER_TOKEN=...`).
4. Update the three prompt/script files (worker-iteration.ps1, worker-prompt.md, scanner-prompt.md).
5. Restart the API so the new env value is picked up.
6. Re-enable the schedulers.
