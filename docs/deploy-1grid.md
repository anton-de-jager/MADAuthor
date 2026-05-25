# Deploying MadAuthor.Api to 1-grid (Plesk Windows / IIS)

This is the end-to-end runbook for getting the API running at
`https://madauthorapi.madprospects.com` against the `madauthor` MSSQL database
on `WINSVRSQL03.hostserv.co.za`.

The deploy is FTP-based because that's what 1-grid Plesk exposes.

---

## 1. One-time setup in the 1-grid Plesk panel

### a. Confirm hosting can run ASP.NET Core 8
On the subscription, open **Hosting Settings** → check that the IIS app
pool offers **ASP.NET Core 8.0 Hosting Bundle** (the Plesk panel calls this
the ".NET Core Hosting Bundle"). If only Framework versions are listed,
open a 1-grid support ticket:

> Please install the .NET 8 ASP.NET Core Hosting Bundle on the IIS app
> pool serving `madauthorapi.madprospects.com`.

### b. Point the domain at an empty folder
- Domain: `madauthorapi.madprospects.com`
- Document root: the folder we'll FTP into (e.g. `/madauthorapi.madprospects.com`)
- Hosting type: **Website**

### c. Set environment variables
Plesk → your domain → **Dotnet Settings** (or **IIS Application Settings**)
→ add these. Replace `<<...>>` with real values.

| Key | Value |
|-----|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | `Server=tcp:WINSVRSQL03.hostserv.co.za,1433;Database=madauthor;User Id=<<DB_USERNAME>>;Password=<<DB_PASSWORD>>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=30;` |
| `ConnectionStrings__Hangfire` | `Server=tcp:WINSVRSQL03.hostserv.co.za,1433;Database=madauthorhangfire;User Id=<<DB_USERNAME>>;Password=<<DB_PASSWORD>>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=30;` |
| `JWT_SIGNING_KEY` | `<<64+ random chars - generate with: openssl rand -hex 64>>` |
| `JWT_ISSUER` | `madauthor` |
| `JWT_AUDIENCE` | `madauthor-web` |
| `STORAGE_LOCAL_ROOT` | `D:\inetpub\vhosts\madprospects.com\madauthorapi.madprospects.com\storage` *(or wherever Plesk puts the site's persistent storage)* |
| `SMTP_HOST` | `smtp.dreamhost.com` |
| `SMTP_PORT` | `465` |
| `SMTP_SECURE` | `true` |
| `SMTP_USER` | `<<SMTP_USER>>` |
| `SMTP_PASS` | `<<SMTP_PASSWORD>>` |
| `SMTP_FROM_ADDRESS` | `<<SMTP_FROM_ADDRESS>>` |
| `SMTP_FROM_NAME` | `MADAuthor` |
| `UNSPLASH_ACCESS_KEY` | `<<optional, for cover image search>>` |

Plesk surfaces these to the app via `<environmentVariables>` in `web.config`
or via the application's process environment - both ways work because we
also have a `web.config` shipping with the deploy that sets
`ASPNETCORE_ENVIRONMENT=Production` defensively.

> **Note:** the API and Hangfire use separate databases: `madauthor` and
> `madauthorhangfire`. `PrepareSchemaIfNecessary=true` creates the Hangfire
> tables in the Hangfire database on first run.

### d. Enable WebSockets on the IIS site (needed for SignalR)
Plesk → your domain → **IIS Settings** → enable **WebSocket Protocol**.
Without this, `/hubs/notifications` will fall back to long-polling and the
client may log "WebSocket failed".

---

## 2. Local prerequisites for the deploy script

```powershell
# .NET 8 SDK installed
dotnet --version       # expect 8.0.x

# PowerShell 5.1+ or PowerShell 7
$PSVersionTable.PSVersion
```

The FTP creds must already live in `.env` at the repo root:

```
API_FTP_HOST=41.185.110.61
API_FTP_USER=coronbyd_0
API_FTP_PASS=<<FTP_PASSWORD>>
API_FTP_PATH=/madauthorapi.madprospects.com
# Optional - set to "true" to use FTPS instead of plain FTP
# API_FTP_USE_TLS=true
```

`.env` is already in `.gitignore`. Do not commit secrets.

---

## 3. Run the deploy

```powershell
./scripts/deploy-api.ps1
```

What it does:
1. `dotnet publish -c Release -o publish/api`
2. Uploads `app_offline.htm` to the site root (IIS unloads the app and releases file locks)
3. Mirrors `publish/api/` to the FTP target, skipping `logs/` and `storage/`
4. Deletes `app_offline.htm` (IIS reloads the app)

Useful flags:

| Flag | What it does |
|------|--------------|
| `-NoBuild` | Re-upload the existing `publish/api/` without re-publishing |
| `-DryRun`  | Show every file/dir it would upload, do nothing |

---

## 4. Smoke test

```powershell
# health endpoint must return 200
curl https://madauthorapi.madprospects.com/api/health/ready

# swagger UI in dev mode only; in Production the OpenAPI JSON is still served
curl https://madauthorapi.madprospects.com/swagger/v1/swagger.json
```

If the app fails to start (HTTP 500.30 from IIS), flip `stdoutLogEnabled`
to `"true"` in `web.config`, redeploy, reproduce the error, then read the
log file under `logs/stdout_<timestamp>.log` over FTP. Flip it back to
`"false"` once you're done - Plesk won't auto-rotate those.

---

## 5. Common 1-grid gotchas

| Symptom | Cause | Fix |
|---------|-------|-----|
| `HTTP Error 500.19 - Internal Server Error` (web.config not valid) | Hosting bundle missing | Ticket 1-grid to install ASP.NET Core 8 Hosting Bundle |
| `HTTP Error 500.30 - ANCM In-Process Start Failure` | App threw during startup | Enable `stdoutLogEnabled` in web.config, read the log |
| `An error occurred while accessing... DefaultConnection` | DB env var not set or wrong | Re-check `ConnectionStrings__DefaultConnection` in Plesk; restart the app pool |
| SignalR client gets 404 on `/hubs/notifications` | WebSockets disabled on IIS site | IIS Settings → enable WebSocket Protocol |
| FTP upload hangs at random files | Passive-mode port range blocked | Set `API_FTP_USE_TLS=true` and retry, or use active mode (edit script to set `UsePassive=$false`) |
| Build publishes a different `web.config` than the one in the repo | SDK transform overwrote it | We set `<IsTransformWebConfigDisabled>true</IsTransformWebConfigDisabled>` in the csproj - confirm it's still there |

---

## 6. Rolling back

```powershell
./scripts/deploy-api.ps1 -NoBuild
```
Re-uploads the **last** `publish/api/` you built locally. So if you have a
known-good local publish folder, this is your rollback. For more robust
rollback, copy the publish folder somewhere (`publish/api.2026-05-20`) after
each successful deploy and re-point the script if needed.

---

## 7. After it's running - security cleanup

The following items should be done **before** the API is live to anyone
external:

- [ ] Rotate the SQL password in the 1-grid panel; update the
      env var in Plesk.
- [ ] Rotate the FTP password; update `.env`.
- [ ] Rotate the SMTP password; update both Plesk env and `.env`.
- [ ] Confirm the seeded admin user has been deleted or password-rotated.
- [ ] Verify HTTPS is enforced - Plesk → SSL/TLS Certificates → "Permanent
      SEO-safe 301 redirect from HTTP to HTTPS".
- [ ] In `appsettings.Production.json`, double-check there is **no** real
      connection string committed (we already cleared it; verify before
      pushing to git).
