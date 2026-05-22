# Claude Code Desktop worker — setup

This folder holds the standing prompt and SQL helpers for the AI worker. The actual work is done by [`apps/api/MadAuthor.Worker`](../../apps/api/MadAuthor.Worker) (a typed CLI the agent invokes for each DB operation).

See `docs/03-worker-and-job-lifecycle.md` for the design rationale.

## One-time setup

### 1. Build the worker CLI

```powershell
cd C:\Code\madauthor\apps\api
dotnet build MadAuthor.Worker -c Release
```

This produces `MadAuthor.Worker/bin/Release/net8.0/madauthor-worker.exe`. The exe reads the same `appsettings.json` / `appsettings.Local.json` / `.env` as the API — no separate config.

### 2. Smoke-test the CLI

```powershell
cd C:\Code\madauthor\apps\api
.\MadAuthor.Worker\bin\Release\net8.0\madauthor-worker.exe heartbeat
.\MadAuthor.Worker\bin\Release\net8.0\madauthor-worker.exe claim
```

If `claim` prints `{"ok":true,"claimed":false}` you're good — no jobs pending. If a job is pending (you ran the create-book wizard in the web app), it'll print job details. Don't worry: the claim is now held for 15 minutes; the next cycle will see it as `InProgress`.

### 3. Schedule a Claude Code session

Open Claude Code in `C:\Code\madauthor` and run:

```
/schedule
```

Configure:

- **Cadence:** `*/1 8-22 * * *` (every minute, 8am–10pm) plus `*/15 * * * *` (every 15 min off-hours).
- **Prompt:** `Read and execute the instructions in workers/claude-desktop/PROMPT.md.`

That's it. The schedule wakes Claude Code, the prompt makes it claim a job, run the Planner, write chapters, mark the job Completed, then exit.

## Manual test (one cycle)

If you want to dry-run without waiting for the schedule:

1. In Claude Code (this repo), say: *"Read `workers/claude-desktop/PROMPT.md` and execute its loop."*
2. Watch the transcript — it should claim one job and complete it within ~30s.
3. Refresh the book detail page in the Angular app. Chapters should appear.

## Where credentials come from

The worker CLI loads (in order, last wins):

1. `apps/api/MadAuthor.Worker/bin/Release/net8.0/appsettings.json` (if present after build copy)
2. `appsettings.Local.json` (gitignored, recommended for creds)
3. Environment variables — including those from `.env` at the repo root (loaded via DotNetEnv on startup)

For local dev, your existing root `.env` (with `DB_HOST`/`DB_USERNAME`/`DB_PASSWORD`/`DB_DATABASE`) is enough — but only if you run the worker from a directory the `.env` walker can reach. Easiest: run from the repo root.

## Future: dedicated `madauthor_worker` SQL login

For now, the worker uses the same SQL login as the API (`madproducts` per your `.env`). The architecture doc calls for a separate `madauthor_worker` principal with limited grants. Add when convenient:

```sql
CREATE LOGIN madauthor_worker WITH PASSWORD = '<strong-random>';
USE madapi;
CREATE USER madauthor_worker FOR LOGIN madauthor_worker;
GRANT SELECT ON dbo.BookProjects, dbo.BookRequests, dbo.BookChapters,
                dbo.BookCharacters, dbo.AIJobQueue TO madauthor_worker;
GRANT INSERT, UPDATE ON dbo.AIJobQueue, dbo.BookChapters, dbo.BookCharacters,
                       dbo.WorkerHeartbeats, dbo.BookAssets TO madauthor_worker;
GRANT UPDATE ON dbo.BookProjects TO madauthor_worker; -- progress + workflow stage
```

Then point the worker at a separate connection string via `appsettings.Local.json`.
