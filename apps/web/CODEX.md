# Project Agent Notes

## Update 2026-05-25
- Updated Angular dependencies in package.json to next (Angular 22 release-candidate channel).
- Removed local node_modules to force clean reinstall behavior.


## Migration Update (2026-05-25)
- Workspace migration finalized under `C:\\Code\\madprospects`; legacy source directories in `C:\\Code` were removed after true move.
- pnpm shared store remains centralized at `C:/Code/.pnpm`; `pnpm approve-builds --all` was run in active workspace contexts.
- Angular dependencies were normalized to `22.0.0-rc.1` for the web app.
- Fixed malformed `pnpm-workspace.yaml` build-approval placeholders in `madauthor/apps/web` and set deterministic `allowBuilds` booleans.

