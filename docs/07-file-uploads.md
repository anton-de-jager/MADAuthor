# 07 — File uploads for manuscripts

The create-book wizard currently only accepts pasted text. Anton wants to upload Word docs, PDFs, and plain-text manuscripts so the AI worker can read them as `existingContent`.

**Headline finding:** the backend for this is ~80% built already. The work is mostly frontend + one ordering bug. This doc enumerates what exists, the one real gap, the plan, and the decisions I need from Anton before writing code.

## 1. What already exists (verified by reading the code, not assumed)

- **Entity:** `BookAsset` (id, BookProjectId, AssetType, FileName, StorageProvider, BlobContainer, BlobKey, MimeType, FileSize, ChecksumSha256, ScanStatus, CreatedDate). Multi-tenant via the parent BookProject's CompanyId.
- **Storage abstraction:** `IFileStorage` (`SaveAsync`, `OpenRead`, `DeleteAsync`, `ResolvePath`). One implementation today: `LocalFileStorage`, writing under `STORAGE_LOCAL_ROOT` (env var, falls back to `AppContext.BaseDirectory/storage`). The interface was deliberately designed so we can swap in `AzureBlobFileStorage` or `S3FileStorage` later without touching callers — Phase 4+ work.
- **Controller:** `BookAssetsController` exposes:
  - `GET    /api/books/{projectId}/assets` — list
  - `POST   /api/books/{projectId}/assets/upload` — multipart `IFormFile`, 50 MB hard cap, MIME whitelist (txt, md, pdf, doc, docx, png, jpg, webp, mp3, wav, m4a)
  - `GET    /api/books/{projectId}/assets/{assetId}/download`
  - `DELETE /api/books/{projectId}/assets/{assetId}`
- **Text extraction:** `ITextExtractor` exists and supports PDF + Word. On upload, the controller calls `TryExtractAndAppend`, which finds the **most recent BookRequest** for the project and appends extracted text to its `ExistingContent` field with a `--- Extracted from upload: {fileName} ---` marker. The worker already reads `request.existingContent` via the `context` subcommand, so once the text reaches that column, the AI sees it.
- **Storage layout:** files land at `{STORAGE_LOCAL_ROOT}/uploads/{CompanyId}/{ProjectId}/{AssetId}-{filename}`. Path traversal blocked at the storage layer.
- **Auth + tenant scoping:** every controller action validates `OwnerUserId` and `CompanyId` against the current user.

## 2. The one real gap (besides UI)

The extraction code in `BookAssetsController.TryExtractAndAppend` does this:

```csharp
var request = await db.BookRequests
    .Where(r => r.BookProjectId == projectId)
    .OrderByDescending(r => r.CreatedDate)
    .FirstOrDefaultAsync(ct);
if (request is null) return;   // silent drop
```

In the current wizard flow, the BookRequest is created at "Submit" time, **after** the project exists. So if we add a file-upload control to wizard step 3, the order is:

1. User reaches step 3, picks a file → `POST /assets/upload` runs.
2. Extraction runs, finds **no BookRequest yet**, silently drops the text.
3. User clicks Submit → BookRequest is created → ExistingContent is empty.

The asset row and the file on disk survive, but the extracted text never reaches the AI. This is the actual blocker, and it's not a UI bug — it's an ordering assumption baked into the upload controller.

## 3. Plan

### 3.1 Backend (small)

Add `ExtractedText nvarchar(max) NULL` to `BookAsset`. Extraction runs at upload time and writes to **the asset row**, not to a BookRequest. Then at BookRequest creation (the existing `Submit` endpoint), stitch all of the project's asset `ExtractedText` into `ExistingContent` with the same markers. One migration, one new column, ~15 lines of controller change.

Why this and not "just re-run extraction at submit time": extraction can be slow (PDFs especially), users can upload several files, and re-doing it at submit means a multi-second Submit click. Doing it on upload spreads the work across the user's natural pace. The asset row is also the right place for the text conceptually — it's a property of the file, not of the request.

### 3.2 Frontend (medium)

- New service: `BookAssetsApi` in `apps/web/src/app/core/api/` — typed wrappers for list / upload / delete / download.
- Step 3 of the create-book wizard gets a drop zone + file list above the existing "Existing content" textarea. The textarea stays — it's still the right input for pasting.
- Upload happens **after** the BookProject is created. That means we need to split the current wizard "Submit" action: when the user advances from step 1 to step 2 (or step 2 to step 3 — I lean step 1→2), the frontend calls `POST /api/books` immediately, gets back the `projectId`, and holds it. Subsequent uploads target that projectId. Final "Submit" on step 4 calls `POST /api/books/{id}/requests`.
- Side effect: if the user cancels the wizard after creating the project, we have an orphan. Either accept that (and add an "Archive" pass later), or call `DELETE /api/books/{id}` on cancel. I lean accept-orphan with a "draft" flag.
- Validation: drop the `requirePromptOrExistingContent` cross-field validator we just added (or extend it to "prompt OR existingContent OR ≥1 attachment").

### 3.3 Worker (zero)

No worker changes needed for text extraction — the text already flows through `BookRequest.ExistingContent`. The worker's `context` subcommand already reads that column.

**Optional follow-up:** expose the asset list (filename + type) to the worker as a separate context field so the AI can reference uploaded images for cover ideas, or differentiate "user pasted this" from "user uploaded a 200-page transcript." Not required for Anton's stated need.

## 4. Decisions I need from Anton

1. **Draft projects.** Confirm I should create the BookProject early (when the user advances past step 1) so uploads have a `projectId` to attach to. Alternative: defer all uploads to after Submit, then redirect to the book detail page with an upload widget there — slower for the user but no orphan-on-cancel concern.
2. **PDF extraction now or later.** The codebase says PDF+Word extraction already works. Want me to verify with a real PDF before this doc gets implemented, or trust it?
3. **Image / audio uploads in the wizard.** The MIME whitelist allows images and audio. Wizard scope = manuscripts only (txt/md/pdf/doc/docx), or accept everything? My lean: manuscripts-only in the wizard, and put images/audio behind a "Project assets" tab on the book detail page later.
4. **Storage location for production.** `LocalFileStorage` writes to disk on the API host. On 1grid shared hosting this is fine for now but capped by the hosting plan's disk quota. Defer S3/Azure to Phase 4 per the existing storage abstraction comment, or address now? I lean defer.

## 5. Out of scope

- Virus scanning (`ScanStatus.Skipped` is hardcoded). Documented as "Phase 4: wire actual virus scan" in the existing controller.
- Per-company storage quotas. There's a 50 MB per-file cap but no aggregate limit.
- OCR for image-of-text uploads.
- Re-extraction if a user replaces a file with the same name.

## 6. Estimate

- Backend migration + extraction relocation: ~1 focused session.
- Frontend service + wizard wiring + drop zone: ~2 focused sessions.
- Manual end-to-end test with a real .docx: ~30 min.

Total: roughly half a day of focused work, plus the four decisions above.
