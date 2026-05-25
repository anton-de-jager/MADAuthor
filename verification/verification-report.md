# MADAuthor Verification Report

Generated: 2026-05-24 16:23 SAST

## Summary

Result: pass for local validation after repairs.

Validated with:
- API: `http://localhost:5150`
- Web: `http://127.0.0.1:4319`
- Database: LocalDB `MadAuthorLocal`
- Hangfire database: LocalDB `MadAuthorLocalHangfire`
- Node runtime for Angular: `v22.17.1`
- pnpm: `11.2.2`
- pnpm store: `C:\Code\.pnpm\v11`

## Fixes Applied

- Standardized frontend package management on pnpm:
  - removed npm lockfile usage (`apps/web/package-lock.json`)
  - added pnpm lock/config files
  - kept package store at `C:\Code\.pnpm`
  - restored the app-local pnpm linker under `apps/web/node_modules/.pnpm` so Angular dev-server resolves dependencies correctly
- Installed/used supported Node `22.17.1` for Angular 19 validation and added `apps/web/.node-version` plus package `engines`.
- Disabled Angular dev-server HMR in `npm start`/`pnpm start` to avoid runtime HMR warning noise during validation.
- Changed user-facing operator navigation/page copy from `Claude Tasks` to `MAD Cloud`; internal `ClaudeTask` API/DTO names remain unchanged.
- Removed stale deployment/database/domain references:
  - `madai`, `madaiapi`, `madapi`, `madleads.ai`, `findrisk`, and old direct IP SQL examples
  - standardized docs to `madauthor`, `madauthorhangfire`, `madauthor.madprospects.com`, and `madauthorapi.madprospects.com`
- Confirmed `C:\Code\node_modules` is absent after cleanup.

## Build And Test Evidence

- `dotnet restore apps\api\MadAuthor.sln`: passed.
- `dotnet build apps\api\MadAuthor.sln`: passed, 0 warnings, 0 errors.
- `dotnet test apps\api\MadAuthor.sln --no-build`: passed, 52/52.
- `pnpm exec ng build` from `apps/web` with Node `v22.17.1`: passed.
- Angular output: `apps\web\dist\web`.
- Frontend runtime log: `verification/logs/web-run-20260524-160235.log`.
- Frontend stderr: `verification/logs/web-run-20260524-160235.err.log` (0 bytes).
- API runtime log: `verification/logs/api-run-20260524-160235.log`.
- API stderr: `verification/logs/api-run-20260524-160235.err.log` (0 bytes).

Runtime warning/error scans:
- API logs: no `WRN`, `ERR`, or `FTL` entries found after restart.
- Web logs: no warning/error/failed/unsupported/module-resolution entries found after restart.
- Browser console: no warnings/errors captured across validated pages.

## Database

- `/api/health/ready`: `200 {"status":"ready","db":true,...}`.
- EF migrations applied:
  - `20260520153424_InitialCreate`
  - `20260520171242_AddAttributionJsonToBookAsset`
  - `20260520195945_AddExtractedTextToBookAsset`
  - `20260521100343_AddClaudeTaskSystem`
  - `20260522085828_AddBodyFontToBookProject`
  - `20260522094844_AddDesignedAssetIdToBookCover`
- `dotnet ef migrations list --no-build`: listed all migrations without pending markers.
- Schema integrity: 27 base tables present.
- Seed data verified:
  - Roles: `Admin`, `Author`, `Owner`, `User`
  - Publishing platforms: `Amazon KDP`, `Barnes & Noble Press`, `Direct Web`, `Gumroad`, `IngramSpark`, `Lulu`, `Shopify`
  - App settings: `workerActive=true`, `scannerActive=true`, `deployNext=false`

## Branding

- Logo/static assets served with HTTP 200:
  - `/logo-wide-MADAuthor.png`
  - `/logo-MADAuthor.png`
  - `/icon-MADAuthor.png`
- Favicon/PWA assets served with HTTP 200:
  - `/favicon.png`
  - `/favicon.ico`
  - `/manifest.webmanifest`
- Loading screen uses `logo-wide-MADAuthor.png`, MADAuthor title text, and the current dark/brand palette.
- Login, shell, landing/home, and MAD Cloud admin surfaces use MADAuthor branding.
- Email confirmation subject/body use MADAuthor branding.
- Repository sweep found no remaining stale `madai`, `madaiapi`, `madapi`, `madleads.ai`, `findrisk`, old direct SQL IP, or sample old-domain references.
- Hardcoded colors found are current Tailwind palette values or intentional book-cover/export template colors, not stale brand colors.

## Browser And Runtime QA

Screenshots:
- `verification/screenshots/home-desktop-20260524-160235.png`
- `verification/screenshots/home-mobile-20260524-160235.png`
- `verification/screenshots/login-redirect-20260524-160235.png`
- `verification/screenshots/dashboard-after-login-20260524-160235.png`
- `verification/screenshots/dashboard-after-reload-20260524-160235.png`
- `verification/screenshots/mad-cloud-admin-20260524-160235.png`
- `verification/screenshots/login-after-logout-20260524-160235.png`

Browser checks:
- `/home` rendered successfully on desktop.
- `/home` rendered successfully at mobile viewport `390x844`.
- Unauthenticated `/dashboard` redirected to `/login`.
- Login page rendered and accepted the disposable confirmed Admin/Owner QA account.
- Dashboard loaded after login.
- Session persisted after dashboard reload.
- `/admin/claude` loaded for Admin/Owner and displayed MAD Cloud branding.
- Logout returned to `/login`.
- Unauthenticated protected-route access after logout redirected to `/login`.
- Browser evidence: `verification/logs/browser-evidence-20260524-160235.json`.

## MAD Cloud

Admin/JWT path:
- Created task ID `1`.
- `GET /api/claude-tasks/next` returned task ID `1`.
- Patched status to `InProgress` (`1`).
- Patched status to `Completed` (`3`) with output notes.
- DB row persisted status `3`.
- Evidence: `verification/logs/mad-cloud-task-evidence-20260524-160235.json`.

Worker-token path:
- Created task ID `2`.
- `GET /api/claude-tasks/next` with `X-Worker-Token` returned task ID `2`.
- Patched status to `InProgress` (`1`) with `X-Worker-Token`.
- Patched status to `Completed` (`3`) with output notes and `X-Worker-Token`.
- DB row persisted status `3`.
- Evidence: `verification/logs/mad-cloud-worker-token-evidence-20260524-160235.json`.

Auth evidence:
- Disposable local confirmed Admin/Owner user created for this run.
- Evidence: `verification/logs/auth-user-20260524-160235.json`.
- Credentials file: `verification/logs/test-user-20260524-160235.txt`.

## Remaining Risks

- Production SQL is intentionally not reachable from this dev machine. Production DB connectivity still must be confirmed after deploy with `https://madauthorapi.madprospects.com/api/health/ready`.
- The parent folder `C:\Code` contains its own broad pnpm workspace. This app now validates with an app-local linker and `C:\Code\.pnpm` store, but future dependency work should be run from `apps/web` with Node `22.17.1`.
- The repo has a large pre-existing dirty worktree. This pass avoided reverting unrelated user changes.
