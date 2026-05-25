# 06 - Deployment

Two paths exist:

- **[FTP / IIS](#ftp--iis-deploy)** - single-shot upload to your existing Windows host. The script you actually run is `deploy/Deploy-Ftp.ps1`.
- **[Fly.io / Docker](#flyio--docker-deploy)** - container-based, portable to any container host. Use if you want to move off the Windows host later.

The Claude Code Desktop worker stays on your local machine in either case (see [03-worker-and-job-lifecycle.md](03-worker-and-job-lifecycle.md)).

---

## FTP / IIS deploy

For a Windows host with IIS and the .NET 8 Hosting Bundle.

### Host prerequisites (one-time)

1. Install **.NET 8.0 Hosting Bundle** from https://dotnet.microsoft.com/download/dotnet/8.0 - this gives IIS the `AspNetCoreModuleV2` it needs.
2. Create an IIS site pointing at the upload destination (e.g. `C:\inetpub\madauthor`).
3. App pool: **No Managed Code** (.NET CLR Version → No Managed Code). ASP.NET Core runs out-of-process.
4. App pool identity needs read/write on the site folder so the app can write the `storage/` and `logs/` subfolders the script creates.
5. If the site uses HTTPS, install a cert (Let's Encrypt via win-acme is free).

### `.env` setup

Add these variables to `C:\Code\madauthor\.env`:

```
FTP_HOST=41.185.110.61
FTP_USER=...
FTP_PASS=...
FTP_PATH=/             # or /madauthor, /httpdocs, etc.
FTP_TLS=true           # use FTPS (recommended). Set false if your host only does plain FTP.
```

These never reach the production server - the deploy script strips them from the uploaded `.env`.

### Deploy

From a regular PowerShell prompt:

```powershell
cd C:\Code\madauthor
.\deploy\Deploy-Ftp.ps1
```

The script will:

1. Build Angular for production (`apps/web/dist/web/browser/`).
2. `dotnet publish` the API for `win-x64`, framework-dependent.
3. Stage everything in `.deploy/staging/` - DLLs + `wwwroot/` (Angular) + `web.config` + a sanitized `.env`.
4. Upload via `curl.exe` (built into Windows 10+) - `--ftp-create-dirs` lets curl mkdir the remote path chain.

Iteration: pass `-SkipBuild` to re-upload from the last staged output without rebuilding.

### After the first deploy

- Recycle the application pool in IIS Manager so it picks up the new DLLs (or restart the site).
- Hit `https://madauthorapi.madprospects.com/api/health/ready` - should return `{ status: "ready", db: true }`.
- Hit the site root - Angular SPA should load same-origin.
- `/swagger` should show the API.
- `/hangfire` works if you're logged in as Admin.

### Known FTP-deploy limitations

- **No diff sync** - the script uploads everything every run. For a faster cadence, use WinSCP's `sync` mode pointing at `.deploy\staging\`.
- **No automated app-pool recycle** - IIS picks up DLL changes within ~5 minutes (file-watcher), or you recycle manually for immediate effect.
- **Same caveats as Fly.io** about SQL firewall: `WINSVRSQL03.hostserv.co.za,1433` must accept connections from the IIS host's egress IP.

---

## Fly.io / Docker deploy

Alternative path that builds a single container.

## Why this architecture

- **One image, one deploy.** Angular is built into `wwwroot/` and served by ASP.NET. Same-origin in prod means no CORS and no SameSite cookie gymnastics.
- **Stateful pieces are external.** SQL Server at `WINSVRSQL03.hostserv.co.za,1433` continues to be the data plane. The container only has a small persistent volume for uploaded files + rendered exports.
- **The worker is local.** When your laptop's Claude Code schedule is running, generation works. When it's not, the API still serves users - they just see jobs stuck in `Pending` until the worker comes back.

## Prerequisites

1. **Docker Desktop** installed (https://www.docker.com/products/docker-desktop). Used to build the image locally first as a sanity check.
2. **`flyctl`** installed (https://fly.io/docs/hands-on/install-flyctl/). Mac/Linux: `curl -L https://fly.io/install.sh | sh`. Windows: `iwr https://fly.io/install.ps1 -useb | iex`.
3. **A Fly.io account** (https://fly.io/app/sign-up). Free tier covers a 512 MB shared-CPU machine plus 3 GB of volume.
4. **An Unsplash Access Key** if you want the cover picker to work in prod.

## SQL firewall

Critical: SQL Server at `WINSVRSQL03.hostserv.co.za,1433` must accept connections from Fly.io's egress IPs. Two options:

- **Allow Fly's egress IPs.** Run `fly ips list` after the first deploy and add them to your SQL firewall. Fly machines share a pool, so it's a few IPs to whitelist.
- **Allow `0.0.0.0/0` with strong SQL auth** (your current `madprospects` user). Less ideal, but pragmatic for a solo project on a non-PCI workload.

If neither is acceptable, host SQL on Azure Database for SQL or AWS RDS instead.

## Local build sanity check

Before pushing to Fly, build the image locally:

```powershell
cd C:\Code\madauthor
docker build -t madauthor:local .
```

That takes ~5 minutes on a cold cache. If it succeeds, you've validated the Dockerfile.

Smoke-test it against your real DB:

```powershell
docker run --rm -p 8080:8080 `
  -e "ConnectionStrings__DefaultConnection=Server=tcp:WINSVRSQL03.hostserv.co.za,1433;Database=madauthor;User Id=<your sql user>;Password=<your sql password>;TrustServerCertificate=True;Encrypt=True;" `
  -e "ConnectionStrings__Hangfire=Server=tcp:WINSVRSQL03.hostserv.co.za,1433;Database=madauthorhangfire;User Id=<your sql user>;Password=<your sql password>;TrustServerCertificate=True;Encrypt=True;" `
  -e JWT_SIGNING_KEY=<paste your signing key> `
  -e UNSPLASH_ACCESS_KEY=<paste your unsplash key> `
  -e SMTP_HOST=smtp.dreamhost.com `
  -e SMTP_PORT=465 `
  -e SMTP_SECURE=true `
  -e SMTP_USER=<your smtp user> `
  -e "SMTP_PASS=<your pass>" `
  madauthor:local
```

Hit `http://localhost:8080`. You should see the Angular app. `/swagger` should show the API.

## First deploy

```powershell
cd C:\Code\madauthor
flyctl auth login                  # browser sign-in
flyctl launch --no-deploy --copy-config --name madauthor --region jnb
# (it'll reuse the fly.toml in this repo)

# Push the secrets so they're available at runtime.
flyctl secrets set `
  ConnectionStrings__DefaultConnection="Server=tcp:WINSVRSQL03.hostserv.co.za,1433;Database=madauthor;User Id=<your sql user>;Password=<your sql password>;TrustServerCertificate=True;Encrypt=True;" `
  ConnectionStrings__Hangfire="Server=tcp:WINSVRSQL03.hostserv.co.za,1433;Database=madauthorhangfire;User Id=<your sql user>;Password=<your sql password>;TrustServerCertificate=True;Encrypt=True;" `
  JWT_SIGNING_KEY="<your signing key>" `
  UNSPLASH_ACCESS_KEY="<your unsplash key>" `
  SMTP_HOST=smtp.dreamhost.com `
  SMTP_PORT=465 `
  SMTP_SECURE=true `
  SMTP_USER="<your smtp user>" `
  SMTP_PASS="<your smtp pass>" `
  SMTP_FROM_ADDRESS="<your from address>" `
  SMTP_FROM_NAME="MADAuthor"

# Create the persistent volume for uploads/exports (3 GB on free tier).
flyctl volumes create madauthor_storage --region jnb --size 3

flyctl deploy
```

Fly returns a URL like `https://madauthor.fly.dev`. Open it.

## Subsequent deploys

```powershell
flyctl deploy
```

That's it. Fly rebuilds the image, runs the new one alongside the old one until it's healthy, then cuts over.

## Custom domain

```powershell
flyctl certs add madauthor.madprospects.com
# Add the CNAME flyctl prints to your DNS.
# Wait a minute, then:
flyctl certs check madauthor.madprospects.com
```

## What to watch in prod

| Concern | Where to look |
| --- | --- |
| Worker not running | `/admin/queue` heartbeat card. Empty / stale → start Claude Code locally + run the schedule. |
| Pipeline progress | `/admin/queue` jobs table. Failed rows show error inline; click Retry. |
| Hangfire dashboard | `https://madauthor.fly.dev/hangfire` (Admin role required). |
| Logs | `flyctl logs` |
| App restart | `flyctl apps restart madauthor` |
| SSH into the running container | `flyctl ssh console` |

## Known limitations of this deploy

1. **No worker in the cloud.** As above. Single-user, on-prem worker by design.
2. **Local file storage.** Files live in the Fly volume. If you outgrow 3 GB or want multi-region redundancy, swap `IFileStorage` to `AzureBlobFileStorage` (already abstracted - one DI line).
3. **SMTP for outbound only.** Inbound mail not supported.
4. **No CDN.** Static Angular assets are served directly by Kestrel. For a global audience, front it with Cloudflare (`flyctl certs add` then point CF in front).
5. **Single Fly machine.** No multi-region scale. Fine for solo + small team. Adding a second machine in `lhr` or `iad` is a one-line config change later.

## Cost expectations

- Fly free tier: covers 1 × 512 MB shared-CPU machine continuously running + 3 GB volume. Practically free for a solo project.
- Once you cross the free tier (multiple machines, larger memory, more volume), expect ~$5–10/month for a real-but-small deploy.
- SQL Server is whatever your current `WINSVRSQL03.hostserv.co.za,1433` host costs - nothing changes.
- Anthropic / Unsplash / SMTP - same as local, billed on your subscriptions.
