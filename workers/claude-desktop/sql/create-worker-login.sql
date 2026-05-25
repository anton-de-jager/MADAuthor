-- create-worker-login.sql
-- Creates a dedicated SQL login the Claude Code Desktop worker can use, with limited grants.
--
-- Run as a sysadmin once. Replace <STRONG_PASSWORD> with a generated password and store it in
-- workers/claude-desktop/.env (or your appsettings.Local.json) so the worker picks it up via
-- a custom DB_USERNAME/DB_PASSWORD pair distinct from the API's connection.
--
-- The grants below match the tables touched by `madauthor-worker` subcommands. If you add a
-- new write target (e.g. BookCovers when Phase 5 lands), grant accordingly.

USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'madauthor_worker')
BEGIN
    CREATE LOGIN madauthor_worker
        WITH PASSWORD = N'<STRONG_PASSWORD>',
             CHECK_POLICY = ON;
END
GO

USE madauthor;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'madauthor_worker')
BEGIN
    CREATE USER madauthor_worker FOR LOGIN madauthor_worker;
END
GO

-- Reads (needed for `context`, `claim` planning, `complete` lookups)
GRANT SELECT ON dbo.BookProjects     TO madauthor_worker;
GRANT SELECT ON dbo.BookRequests     TO madauthor_worker;
GRANT SELECT ON dbo.BookChapters     TO madauthor_worker;
GRANT SELECT ON dbo.BookCharacters   TO madauthor_worker;
GRANT SELECT ON dbo.BookAssets       TO madauthor_worker;
GRANT SELECT ON dbo.Authors          TO madauthor_worker;
GRANT SELECT ON dbo.AIJobQueue       TO madauthor_worker;

-- Writes (claim, progress, complete, fail, planning/chapter writes)
GRANT INSERT, UPDATE ON dbo.AIJobQueue          TO madauthor_worker;
GRANT INSERT, UPDATE ON dbo.BookChapters        TO madauthor_worker;
GRANT INSERT         ON dbo.BookCharacters      TO madauthor_worker;
GRANT INSERT         ON dbo.BookAssets          TO madauthor_worker;
GRANT INSERT, UPDATE ON dbo.WorkerHeartbeats    TO madauthor_worker;

-- BookProjects: worker updates CompletionPercentage / WorkflowStage / Description (metadata) only.
GRANT UPDATE ON dbo.BookProjects TO madauthor_worker;

-- BookRequests: worker reads. UPDATE is intentionally NOT granted.

GO
PRINT 'madauthor_worker login + user created with worker-scoped grants.';
