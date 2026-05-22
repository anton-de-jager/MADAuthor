# 02 — Data model

This document refines the table list in the original spec into a relational schema that EF Core can own. Issues in the spec (duplicate columns, missing FKs, missing indexes, no tenancy column on most rows, a `BookRequests` table with ~50 boolean flags) are called out and resolved here.

## 1. Conventions

- Every table has `Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWSEQUENTIALID()`.
- Audit columns on every table: `CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()`, `UpdatedDate DATETIME2 NULL`.
- Soft delete via `IsDeleted BIT NOT NULL DEFAULT 0` on entities where users can delete (projects, chapters, assets). Hard delete on transient tables (job queue, audit, notifications).
- All tenant-scoped tables have `CompanyId UNIQUEIDENTIFIER NOT NULL` with an index. EF global query filter applies the current `CompanyId`.
- FKs are explicit and named `FK_{Child}_{Parent}_{Column}`. Cascade rules: cascade delete on owned-aggregate tables (e.g. `BookChapters` cascades from `BookProjects`); restrict on cross-aggregate references (e.g. don't cascade delete a `User` to delete their books).
- Enums are stored as `TINYINT` with the C# enum as source of truth. EF maps them.

## 2. Issues found in the original spec and resolutions

| Issue | Resolution |
| --- | --- |
| `BookRequests` has both `IncludeWorkbook` and `IncludeWorkbookVersion`; same for several other near-duplicates. | Collapse all `Include*` flags into a single `Features JSON` column on `BookRequests`. Documented schema below. |
| 50+ bool columns on one table is unmanageable. | Normalize. `BookRequests` keeps structured fields (tone, pov, length, etc.); feature toggles go in JSON. |
| `Companies` has no membership table. How do users join a company? | Add `CompanyMembers (UserId, CompanyId, Role)`. |
| `Authors` is a separate table but most fields belong on `Users`. | Keep `Authors` separate because a single user can have multiple pen names. `Users` stays as the auth identity. |
| `AuditLogs.Changes` is unspecified. | Store as `NVARCHAR(MAX)` containing a JSON diff. |
| `BookAssets` has no storage-provider context. | Add `StorageProvider`, `BlobContainer`, `BlobKey`, `Checksum`, `ScanStatus`. |
| `BookExports` is missing size, expiry, checksum, download count. | Add those. |
| `AIJobQueue` has no concept of worker identity or stage. | Add `ClaimedBy`, `ClaimedAt`, `LockExpiresAt`, `Stage`, `Progress`. |
| Multi-tenant claim but no tenancy column on most rows. | Add `CompanyId` to all tenant-scoped tables. |
| No indexes mentioned. | Each table below lists its non-PK indexes. |
| `Notifications` doesn't track channel or delivery. | Add `Channel`, `DeliveryStatus`, `DeliveredAt`. |

## 3. Tables

### Users

The auth identity. One per person.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| Email | nvarchar(256) NOT NULL | UNIQUE |
| PasswordHash | nvarchar(512) NOT NULL |  |
| FirstName | nvarchar(100) NOT NULL |  |
| LastName | nvarchar(100) NOT NULL |  |
| Phone | nvarchar(40) NULL |  |
| AvatarUrl | nvarchar(1024) NULL |  |
| EmailConfirmed | bit NOT NULL DEFAULT 0 |  |
| MfaEnabled | bit NOT NULL DEFAULT 0 |  |
| MfaSecret | nvarchar(128) NULL | Encrypted at rest |
| IsActive | bit NOT NULL DEFAULT 1 |  |
| LastLoginDate | datetime2 NULL |  |
| FailedLoginAttempts | int NOT NULL DEFAULT 0 |  |
| LockedUntil | datetime2 NULL |  |
| CreatedDate | datetime2 NOT NULL |  |
| UpdatedDate | datetime2 NULL |  |

Indexes: `IX_Users_Email` (unique).

### Companies

A tenant. A `User` can belong to one or more companies via `CompanyMembers`.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| Name | nvarchar(200) NOT NULL |  |
| Slug | nvarchar(80) NOT NULL | UNIQUE — used in URLs |
| LogoUrl | nvarchar(1024) NULL |  |
| BrandingJson | nvarchar(max) NULL | Colors, fonts, theme overrides |
| OwnerUserId | uniqueidentifier NOT NULL | FK Users |
| Plan | tinyint NOT NULL DEFAULT 0 | Free, Pro, Business |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_Companies_Slug` (unique), `IX_Companies_OwnerUserId`.

### CompanyMembers

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| UserId | uniqueidentifier NOT NULL | FK Users |
| CompanyId | uniqueidentifier NOT NULL | FK Companies |
| Role | tinyint NOT NULL | Owner, Admin, Editor, Viewer |
| InvitedDate | datetime2 NULL |  |
| AcceptedDate | datetime2 NULL |  |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_CompanyMembers_UserId_CompanyId` (unique), `IX_CompanyMembers_CompanyId`.

### Authors

A pen name + author profile. A `User` can own many `Authors`.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| UserId | uniqueidentifier NOT NULL | FK Users |
| CompanyId | uniqueidentifier NOT NULL | FK Companies (tenant) |
| PenName | nvarchar(200) NOT NULL |  |
| Biography | nvarchar(max) NULL |  |
| Website | nvarchar(500) NULL |  |
| SocialLinksJson | nvarchar(max) NULL | `{"twitter": "...", "instagram": "..."}` |
| GenresCsv | nvarchar(500) NULL |  |
| PreferredWritingStyle | nvarchar(200) NULL |  |
| DefaultLanguage | nvarchar(10) NOT NULL DEFAULT 'en' |  |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_Authors_UserId`, `IX_Authors_CompanyId`.

### BookProjects

The main aggregate root for a book.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| CompanyId | uniqueidentifier NOT NULL | FK Companies (tenant) |
| OwnerUserId | uniqueidentifier NOT NULL | FK Users |
| AuthorId | uniqueidentifier NULL | FK Authors |
| Title | nvarchar(300) NOT NULL |  |
| Subtitle | nvarchar(300) NULL |  |
| Description | nvarchar(max) NULL |  |
| Genre | nvarchar(100) NULL |  |
| FictionOrNonfiction | tinyint NOT NULL | Fiction, Nonfiction, Mixed |
| TargetAudience | nvarchar(200) NULL |  |
| WritingTone | nvarchar(100) NULL |  |
| Language | nvarchar(10) NOT NULL DEFAULT 'en' |  |
| Status | tinyint NOT NULL | Draft, InProgress, ReadyForReview, Completed, Archived |
| WorkflowStage | tinyint NOT NULL | Intake, Planning, Drafting, Editing, Formatting, Publishing |
| CompletionPercentage | int NOT NULL DEFAULT 0 |  |
| EstimatedPageCount | int NULL |  |
| EstimatedWordCount | int NULL |  |
| TargetWordCount | int NULL |  |
| TargetReadingLevel | nvarchar(50) NULL |  |
| ISBN | nvarchar(20) NULL |  |
| CopyrightText | nvarchar(500) NULL |  |
| PublishingGoal | nvarchar(200) NULL |  |
| Deadline | datetime2 NULL |  |
| RequireOutlineApproval | bit NOT NULL DEFAULT 1 | If true, pipeline pauses after Planner until user approves outline. See [04 §9](04-ai-orchestration.md). |
| OutlineApprovedAt | datetime2 NULL | Set when the user clicks "Approve outline" — gates downstream jobs. |
| IsDeleted | bit NOT NULL DEFAULT 0 |  |
| CreatedDate | datetime2 NOT NULL |  |
| UpdatedDate | datetime2 NULL |  |

Indexes: `IX_BookProjects_CompanyId_Status`, `IX_BookProjects_OwnerUserId`.

### BookRequests

The user's intake: what they submitted and how they want it processed. One `BookRequest` per generation pass. A `BookProject` can have multiple requests over time (initial generation, regeneration of a section, expansion).

Structured columns:

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects, cascade delete |
| RequestType | tinyint NOT NULL | Idea, Outline, Manuscript, Expansion, SermonToBook, NotesToBook, etc. |
| IdeaPrompt | nvarchar(max) NULL |  |
| ExistingContent | nvarchar(max) NULL |  |
| Notes | nvarchar(max) NULL |  |
| AIInstructions | nvarchar(max) NULL | Free-form additional instructions |
| DesiredTone | nvarchar(100) NULL |  |
| DesiredLength | nvarchar(100) NULL | e.g. "60,000 words" |
| POVStyle | nvarchar(50) NULL | First/Third/Omniscient |
| WritingStyle | nvarchar(200) NULL |  |
| EndingType | nvarchar(100) NULL |  |
| ThemesCsv | nvarchar(500) NULL |  |
| KeywordsCsv | nvarchar(500) NULL |  |
| EducationalLevel | nvarchar(50) NULL |  |
| CitationStyle | nvarchar(50) NULL | APA, MLA, Chicago, Turabian |
| Variables | nvarchar(max) NOT NULL | JSON, see §4 |
| Features | nvarchar(max) NOT NULL | JSON, see §5 |
| TargetPlatformsCsv | nvarchar(200) NULL | KDP, IngramSpark, Lulu, Gumroad, Shopify, Web |
| RequestedFormatsCsv | nvarchar(200) NULL | PDF, EPUB, DOCX, PrintPDF |
| Priority | tinyint NOT NULL DEFAULT 5 | 1 (highest) – 10 |
| Status | tinyint NOT NULL | Submitted, Queued, InProgress, Completed, Failed, Cancelled |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_BookRequests_BookProjectId_Status`.

### BookChapters

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects, cascade delete |
| ChapterNumber | int NOT NULL |  |
| Title | nvarchar(300) NOT NULL |  |
| Summary | nvarchar(max) NULL |  |
| ContentMarkdown | nvarchar(max) NULL | Canonical content stored as Markdown |
| ContentHtml | nvarchar(max) NULL | Rendered for preview (regenerated on edit) |
| WordCount | int NOT NULL DEFAULT 0 |  |
| Status | tinyint NOT NULL | Planned, Drafting, Drafted, Editing, Final |
| GeneratedByJobId | uniqueidentifier NULL | FK AIJobQueue, to trace origin |
| CreatedDate | datetime2 NOT NULL |  |
| UpdatedDate | datetime2 NULL |  |

Indexes: `IX_BookChapters_BookProjectId_ChapterNumber` (unique within project).

### BookCharacters

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects, cascade delete |
| Name | nvarchar(200) NOT NULL |  |
| Description | nvarchar(max) NULL |  |
| Personality | nvarchar(max) NULL |  |
| Background | nvarchar(max) NULL |  |
| Goals | nvarchar(max) NULL |  |
| Conflicts | nvarchar(max) NULL |  |
| CreatedDate | datetime2 NOT NULL |  |

### BookAssets

Anything stored in blob storage that belongs to a book.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects, cascade delete |
| AssetType | tinyint NOT NULL | Upload, Cover, Illustration, Audio, Generated |
| FileName | nvarchar(500) NOT NULL |  |
| StorageProvider | tinyint NOT NULL | Local (Phase 1), AzureBlob, S3 |
| BlobContainer | nvarchar(200) NOT NULL |  |
| BlobKey | nvarchar(1024) NOT NULL |  |
| MimeType | nvarchar(200) NOT NULL |  |
| FileSize | bigint NOT NULL |  |
| ChecksumSha256 | char(64) NULL |  |
| ScanStatus | tinyint NOT NULL DEFAULT 0 | Pending, Clean, Infected, Skipped |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_BookAssets_BookProjectId_AssetType`.

### BookExports

A finalized rendered artifact (PDF, EPUB, DOCX) ready to download.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects, cascade delete |
| ExportType | tinyint NOT NULL | Pdf, Epub, Docx, PrintPdfKdp, PrintPdfIngram, Html, Markdown — MOBI deliberately excluded (deprecated by KDP) |
| BlobKey | nvarchar(1024) NULL | Null while Status is Queued/Running |
| FileSize | bigint NULL |  |
| ChecksumSha256 | char(64) NULL |  |
| Status | tinyint NOT NULL | Queued, Running, Ready, Failed |
| ErrorMessage | nvarchar(1000) NULL |  |
| ExpiresAt | datetime2 NULL | Auto-deleted after this |
| DownloadCount | int NOT NULL DEFAULT 0 |  |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_BookExports_BookProjectId_ExportType_Status`.

### BookCovers

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects, cascade delete |
| Prompt | nvarchar(max) NOT NULL |  |
| Style | nvarchar(200) NULL |  |
| AssetId | uniqueidentifier NULL | FK BookAssets — the generated image |
| Status | tinyint NOT NULL | Pending, Generating, Ready, Failed, Selected |
| CreatedDate | datetime2 NOT NULL |  |

### PublishingPlatforms

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| Name | nvarchar(100) NOT NULL | KDP, IngramSpark, Lulu, etc. |
| ApiSettingsJson | nvarchar(max) NULL |  |
| IsEnabled | bit NOT NULL DEFAULT 1 |  |

Seeded; rarely changes.

### Notifications

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| UserId | uniqueidentifier NOT NULL | FK Users |
| CompanyId | uniqueidentifier NOT NULL | FK Companies (tenant) |
| Type | tinyint NOT NULL | JobStarted, JobProgress, JobCompleted, ExportReady, Error, System |
| Title | nvarchar(200) NOT NULL |  |
| Message | nvarchar(1000) NOT NULL |  |
| LinkUrl | nvarchar(500) NULL |  |
| Channel | tinyint NOT NULL | InApp, Email, Sms, WhatsApp |
| DeliveryStatus | tinyint NOT NULL | Pending, Sent, Delivered, Failed |
| DeliveredAt | datetime2 NULL |  |
| IsRead | bit NOT NULL DEFAULT 0 |  |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_Notifications_UserId_IsRead_CreatedDate`.

### AIJobQueue

The contract between API and Claude Code Desktop worker. Critical.

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| BookProjectId | uniqueidentifier NOT NULL | FK BookProjects |
| BookRequestId | uniqueidentifier NULL | FK BookRequests, when applicable |
| JobType | tinyint NOT NULL | PlanBook, ResearchTopic, DraftChapter, EditChapter, ContinuityCheck, GenerateCover, GenerateMetadata, GenerateMarketing |
| Priority | tinyint NOT NULL DEFAULT 5 |  |
| Status | tinyint NOT NULL | Pending, Claimed, InProgress, Completed, Failed, Cancelled |
| Stage | nvarchar(100) NULL | Free-form: "planning", "writing-chapter-3", "editing", etc. |
| Progress | int NOT NULL DEFAULT 0 | 0–100 |
| InputJson | nvarchar(max) NULL | Job-type-specific input |
| OutputJson | nvarchar(max) NULL | Job-type-specific output references |
| ClaimedBy | nvarchar(200) NULL | Worker instance id (machine name + pid) |
| ClaimedAt | datetime2 NULL |  |
| LockExpiresAt | datetime2 NULL | If passed, another worker can re-claim |
| StartedDate | datetime2 NULL |  |
| CompletedDate | datetime2 NULL |  |
| ErrorMessage | nvarchar(2000) NULL |  |
| RetryCount | tinyint NOT NULL DEFAULT 0 |  |
| MaxRetries | tinyint NOT NULL DEFAULT 3 |  |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_AIJobQueue_Status_Priority_CreatedDate` (this is the polling query path — keep it tight).

### WorkerHeartbeats

| Column | Type | Notes |
| --- | --- | --- |
| Id | uniqueidentifier PK |  |
| WorkerId | nvarchar(200) NOT NULL | Machine name + pid |
| LastPing | datetime2 NOT NULL |  |
| LastJobId | uniqueidentifier NULL |  |

Indexes: `IX_WorkerHeartbeats_WorkerId` (unique). Upserted by the worker each cycle.

### AuditLogs

| Column | Type | Notes |
| --- | --- | --- |
| Id | bigint identity PK |  |
| UserId | uniqueidentifier NULL |  |
| CompanyId | uniqueidentifier NULL |  |
| Entity | nvarchar(100) NOT NULL |  |
| EntityId | nvarchar(100) NULL |  |
| Action | nvarchar(50) NOT NULL | Create, Update, Delete, Login, FileUpload, ExportDownload, etc. |
| ChangesJson | nvarchar(max) NULL | `{ "field": { "from": "...", "to": "..." } }` |
| IpAddress | nvarchar(45) NULL |  |
| UserAgent | nvarchar(500) NULL |  |
| CreatedDate | datetime2 NOT NULL |  |

Indexes: `IX_AuditLogs_CompanyId_Entity_CreatedDate`, `IX_AuditLogs_UserId_CreatedDate`.

## 4. BookRequests.Variables JSON shape

The huge list of writing/fiction/non-fiction/Christian/publishing variables from the spec, collapsed into a JSON document:

```jsonc
{
  "writing": {
    "tone": "warm",
    "humorLevel": 3,             // 0–5
    "emotionalIntensity": 4,
    "spiritualDepth": 2,
    "academicLevel": 2,
    "simplicityLevel": 4,
    "storyPacing": "moderate",
    "narrativeComplexity": 3,
    "vocabularySophistication": 3,
    "dialogueFrequency": 4,
    "chapterLength": "medium",   // short, medium, long
    "sentenceComplexity": 3
  },
  "fiction": {                   // null if RequestType is non-fiction
    "worldBuildingDepth": 4,
    "characterDepth": 5,
    "romanceLevel": 2,
    "conflictLevel": 4,
    "plotTwists": 3,
    "suspenseLevel": 4,
    "violenceLevel": 1,
    "fantasyLevel": 5
  },
  "nonfiction": {                // null if RequestType is fiction
    "practicalityLevel": 5,
    "actionability": 4,
    "researchDepth": 4,
    "citationCount": "medium",   // none, few, medium, many
    "exampleFrequency": 4,
    "caseStudyFrequency": 3
  },
  "christian": {                 // null if not applicable
    "biblicalReferences": "abundant",
    "devotionalTone": true,
    "prayerInclusion": true,
    "reflectionQuestions": true,
    "sermonFormatting": false
  },
  "publishing": {
    "trimSize": "6x9",
    "marginsInches": 0.75,
    "bleed": true,
    "typography": "serif",
    "fontFamily": "Garamond",
    "interiorStyle": "classic",
    "coverStyle": "modern",
    "kdpOptimization": true,
    "epubOptimization": true
  }
}
```

Validated at the API boundary against a JSON Schema. Versioned: `"schemaVersion": 1` is required so future schema bumps can migrate cleanly.

## 5. BookRequests.Features JSON shape

Replaces all the `Include*` booleans on the original spec. Keys are the feature, value is `true`/`false` or an object with options:

```jsonc
{
  "workbook": true,
  "discussionQuestions": true,
  "illustrations": false,
  "aiImages": false,
  "references": true,
  "studyGuide": false,
  "actionSteps": true,
  "devotional": false,
  "sermonExpansion": false,
  "researchExpansion": true,
  "caseStudies": true,
  "quotes": true,
  "bibleVerses": false,
  "journalingPrompts": false,
  "reflectionQuestions": true,
  "worksheets": false,
  "marketingAssets": true,
  "audioVersion": false,
  "videoScripts": false,
  "courseVersion": false,
  "presentationSlides": false,
  "teacherGuide": false,
  "kidsVersion": false,
  "translation": { "enabled": false, "languages": [] }
}
```

The original spec had `IncludeWorkbook` AND `IncludeWorkbookVersion` (and similar pairs). Resolved by keeping a single key per feature.

## 6. Relationships diagram (textual)

```
Users 1───* Authors
Users 1───* CompanyMembers *───1 Companies
Users 1───* BookProjects
Companies 1───* BookProjects
BookProjects 1───* BookRequests
BookProjects 1───* BookChapters
BookProjects 1───* BookCharacters
BookProjects 1───* BookAssets
BookProjects 1───* BookExports
BookProjects 1───* BookCovers
BookProjects 1───* AIJobQueue (via BookProjectId)
BookRequests 1───* AIJobQueue (via BookRequestId, optional)
AIJobQueue 1───* BookChapters (via BookChapters.GeneratedByJobId)
Users 1───* Notifications
```

## 7. Migration & seed strategy

- EF Core migrations live under `apps/api/MadAuthor.Infrastructure/Migrations/`. The `db/migrations/` folder at the root is just a symlink/mirror for visibility.
- Initial migration creates the full schema above.
- Seed script populates: `PublishingPlatforms` (KDP, IngramSpark, Lulu, B&N Press), one dev `Company` ("MADAuthor Dev"), one dev `User` (Anton), one dev `Author`.
- Subsequent migrations are additive — never destructive — and tested with a "migrate from prior version" script in CI.

## 8. Out of scope for the data model

The spec implies but does not commit to:

- **Billing/subscriptions** — Stripe customer/subscription/invoice tables. Deferred unless Phase 1 needs paid tiers.
- **Collaboration** — sharing a project with a non-owner. Mentioned in "Collaboration updates" under realtime; needs `BookProjectMembers (UserId, BookProjectId, Role)` when implemented.
- **Prompt-template store** — the spec mentions "Prompt Templates" as a UI page. If users can edit templates in-app, add `PromptTemplates (Id, CompanyId, Name, Body, Variables, Version)`. For Phase 1, prompt templates live in the repo as Markdown files under `packages/prompts/`.
- **API keys for tenants** — if users can call MADAuthor via API, add `ApiKeys (Id, UserId, CompanyId, KeyHash, Scopes, ExpiresAt)`.
