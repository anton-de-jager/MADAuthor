# MADAuthor — Claude project notes

AI-assisted book authoring platform. .NET 8 API + Angular SPA, MSSQL,
Hangfire (separate `madauthorhangfire` DB). Deployed to 1-grid Plesk
(FTP-based, with IIS recycle hook).

Full architecture lives in `docs/01-architecture.md` … `docs/08-claude-task-system.md`.
This file is the short cheat-sheet.

## Canonical infrastructure values (source of truth: `C:\Code\MAD\ToDo.xlsx`)

| Thing             | Value                                       |
|-------------------|---------------------------------------------|
| Frontend URL      | `https://madauthor.madprospects.com`       |
| API URL           | `https://madauthorapi.madprospects.com`    |
| SQL host          | `WINSVRSQL03.hostserv.co.za,1433`           |
| App DB            | `madauthor`                                 |
| Hangfire DB       | `madauthorhangfire` (separate DB, not a schema) |
| Hangfire dashboard| `/hangfire` (Admin/Owner-gated via JWT)     |
| FTP host          | `41.185.110.61`                             |
| API FTP path      | `/madauthorapi.madprospects.com`           |
| FE FTP path       | `/madauthor.madprospects.com`              |

DB and SQL host are **not reachable from dev machines** — only from the
deployed 1-grid box. Verify DB connectivity via `/api/health/ready` after
deploy.

## Layout

| Path                                | Purpose                                    |
|-------------------------------------|--------------------------------------------|
| `apps/api/MadAuthor.Api/`           | ASP.NET Core 8 Web API + SignalR + Hangfire dashboard |
| `apps/api/MadAuthor.Api/Program.cs` | Service registration; reads `ConnectionStrings:DefaultConnection` and `ConnectionStrings:Hangfire` (falls back to composing from `DB_*` env vars) |
| `apps/api/MadAuthor.Infrastructure/`| EF Core + persistence + integrations       |
| `apps/api/MadAuthor.Worker/`        | Background pipeline worker                 |
| `apps/web/`                         | Angular 19 + Tailwind SPA                  |
| `apps/web/src/environments/environment.prod.ts` | FE API base URL              |
| `apps/web/public/`                  | Static assets (favicon, branding)          |
| `packages/prompts/`                 | Shared prompt templates                    |
| `workers/claude-desktop/`           | Claude Code Desktop integration            |
| `.claude/scanner/` + `.claude/worker/` | Hourly + per-minute /claude task scheduler scripts (see `.claude/README.md`) |

## Deploy

```powershell
# Full deploy (API + FE) with IIS recycle
pwsh ./deploy.ps1

# Selective
pwsh ./deploy.ps1 -ApiOnly       # API only + recycle
pwsh ./deploy.ps1 -FeOnly        # FE only (no recycle)
pwsh ./deploy.ps1 -SkipBuild     # reuse staging dir
pwsh ./deploy.ps1 -SkipRecycle   # don't touch app_offline.htm
```

`deploy.ps1` is a thin wrapper around `deploy/Deploy-Ftp.ps1` and appends an
IIS recycle by uploading/deleting `app_offline.htm` over FTPS, then warms
the worker via `https://madauthorapi.madprospects.com/api/health/ready`.

`.env` keys (gitignored):
- `DB_HOST`, `DB_USERNAME`, `DB_PASSWORD`, `DB_DATABASE`, `DB_HANGFIRE_DATABASE`
- `API_FTP_HOST`, `API_FTP_USER`, `API_FTP_PASS`, `API_FTP_PATH`
- `FE_FTP_HOST`, `FE_FTP_USER`, `FE_FTP_PASS`, `FE_FTP_PATH`
- `JWT_SIGNING_KEY`, `CLAUDE_WORKER_TOKEN`
- `SMTP_*`
- `UNSPLASH_ACCESS_KEY`, `UNSPLASH_SECRET_KEY`, `UNSPLASH_APP_NAME`

In production on 1-grid, prefer setting `ConnectionStrings__DefaultConnection`
and `ConnectionStrings__Hangfire` in the Plesk panel — the `DB_*` env vars
are only a fallback used to compose the connection string if those are unset.

## After deploy

- `https://madauthorapi.madprospects.com/api/health/ready` → 200
- `https://madauthor.madprospects.com` → SPA loads
- `https://madauthorapi.madprospects.com/hangfire` → 401 (Admin/Owner only). Log in as Admin/Owner first to view the dashboard.

## `/claude` operator queue

See `.claude/README.md` for the end-to-end install of the `MADAuthorClaudeWorker`
and `MADAuthorClaudeScanner` scheduled tasks. Logs land in
`.claude/worker/worker.log` and `.claude/scanner/scanner.log`.

## Known issues

- **Local build fails** because `apps/api/MadAuthor.Infrastructure/Covers/QuestPdfCoverComposer.cs`
  imports `SkiaSharp` but the csproj doesn't reference the `SkiaSharp` NuGet
  package. The deployed build is older than this file. Either add
  `<PackageReference Include="SkiaSharp" Version="2.x" />` to
  `MadAuthor.Infrastructure.csproj` or remove the SkiaSharp use from
  `QuestPdfCoverComposer.cs`.


## Migration Update (2026-05-25)
- Workspace migration finalized under `C:\\Code\\madprospects`; legacy source directories in `C:\\Code` were removed after true move.
- pnpm shared store remains centralized at `C:/Code/.pnpm`; `pnpm approve-builds --all` was run in active workspace contexts.
- Angular dependencies were normalized to `22.0.0-rc.1` for the web app.
- Fixed malformed `pnpm-workspace.yaml` build-approval placeholders in `madauthor/apps/web` and set deterministic `allowBuilds` booleans.

