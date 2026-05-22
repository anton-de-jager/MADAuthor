import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

export type FictionOrNonfiction = 0 | 1 | 2;
export type BookProjectStatus = 0 | 1 | 2 | 3 | 4;
export type BookProjectWorkflowStage = 0 | 1 | 2 | 3 | 4 | 5;
export type BookRequestType = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;
export type BookChapterStatus = 0 | 1 | 2 | 3 | 4;

export interface CreateBookRequest {
  title: string;
  subtitle?: string | null;
  genre?: string | null;
  fictionOrNonfiction: FictionOrNonfiction;
  targetAudience?: string | null;
  writingTone?: string | null;
  language: string;
  targetWordCount?: number | null;
  targetReadingLevel?: string | null;
}

export interface BookSummary {
  id: string;
  title: string;
  subtitle?: string | null;
  genre?: string | null;
  status: BookProjectStatus;
  workflowStage: BookProjectWorkflowStage;
  completionPercentage: number;
  createdDate: string;
}

export interface BookChapterSummary {
  id: string;
  chapterNumber: number;
  title: string;
  summary?: string | null;
  wordCount: number;
  status: BookChapterStatus;
}

export interface OutlineChapterPayload {
  id?: string;
  chapterNumber: number;
  title: string;
  summary?: string | null;
}

export interface BookDetail {
  id: string;
  title: string;
  subtitle?: string | null;
  description?: string | null;
  genre?: string | null;
  fictionOrNonfiction: FictionOrNonfiction;
  targetAudience?: string | null;
  writingTone?: string | null;
  language: string;
  status: BookProjectStatus;
  workflowStage: BookProjectWorkflowStage;
  completionPercentage: number;
  targetWordCount?: number | null;
  targetReadingLevel?: string | null;
  requireOutlineApproval: boolean;
  outlineApprovedAt?: string | null;
  createdDate: string;
  authorId?: string | null;
  authorPenName?: string | null;
  chapters: BookChapterSummary[];
}

export interface UpdateBookRequest {
  title?: string | null;
  subtitle?: string | null;
  genre?: string | null;
  fictionOrNonfiction?: FictionOrNonfiction | null;
  targetAudience?: string | null;
  writingTone?: string | null;
  language?: string | null;
  targetWordCount?: number | null;
  targetReadingLevel?: string | null;
  authorId?: string | null;
}

export interface AuthorSummary {
  id: string;
  penName: string;
}

export interface BookChapterDetail extends BookChapterSummary {
  contentMarkdown: string | null;
  updatedDate: string | null;
}

export interface SubmitBookRequest {
  requestType: BookRequestType;
  ideaPrompt?: string;
  existingContent?: string;
  notes?: string;
  aIInstructions?: string;
  desiredTone?: string;
  desiredLength?: string;
  povStyle?: string;
  writingStyle?: string;
  themesCsv?: string;
  keywordsCsv?: string;
  variables?: unknown;
  features?: unknown;
  targetPlatformsCsv?: string;
  requestedFormatsCsv?: string;
  priority: number;
}

export interface BookCharacterDto {
  id: string;
  name: string;
  description?: string | null;
  personality?: string | null;
  background?: string | null;
  goals?: string | null;
  conflicts?: string | null;
  createdDate: string;
}

export interface CreateBookCharacterRequest {
  name: string;
  description?: string | null;
  personality?: string | null;
  background?: string | null;
  goals?: string | null;
  conflicts?: string | null;
}

export interface BookAsset {
  id: string;
  assetType: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  scanStatus: string;
  createdDate: string;
}

export interface UnsplashSearchResult {
  id: string;
  altDescription?: string | null;
  description?: string | null;
  thumbUrl: string;
  previewUrl: string;
  width: number;
  height: number;
  color?: string | null;
  photographer: { name: string; profileUrl: string; username: string };
  photoUrl: string;
}

export interface BookCoverRow {
  id: string;
  prompt: string;
  style?: string | null;
  assetId?: string | null;
  status: 'Pending' | 'Generating' | 'Ready' | 'Failed' | 'Selected';
  createdDate: string;
  assetUrl?: string | null;
  attribution?: string | null; // JSON string
}

export interface BookExportRow {
  id: string;
  exportType: 'Pdf' | 'Epub' | 'Docx' | 'PrintPdfKdp' | 'PrintPdfIngram' | 'Html' | 'Markdown';
  status: 'Queued' | 'Running' | 'Ready' | 'Failed';
  fileSize?: number | null;
  errorMessage?: string | null;
  expiresAt?: string | null;
  downloadCount: number;
  createdDate: string;
}

@Injectable({ providedIn: 'root' })
export class BooksApi {
  private http = inject(HttpClient);

  list(): Observable<BookSummary[]> {
    return this.http.get<BookSummary[]>('/api/books');
  }

  create(req: CreateBookRequest): Observable<BookSummary> {
    return this.http.post<BookSummary>('/api/books', req);
  }

  get(id: string): Observable<BookDetail> {
    return this.http.get<BookDetail>(`/api/books/${id}`);
  }

  update(id: string, req: UpdateBookRequest): Observable<BookDetail> {
    return this.http.patch<BookDetail>(`/api/books/${id}`, req);
  }

  listAuthors(): Observable<AuthorSummary[]> {
    return this.http.get<AuthorSummary[]>('/api/books/authors');
  }

  getChapter(id: string, chapterId: string): Observable<BookChapterDetail> {
    return this.http.get<BookChapterDetail>(`/api/books/${id}/chapters/${chapterId}`);
  }

  submit(id: string, req: SubmitBookRequest): Observable<{ bookRequestId: string; jobId: string }> {
    return this.http.post<{ bookRequestId: string; jobId: string }>(
      `/api/books/${id}/requests`,
      req,
    );
  }

  approveOutline(id: string): Observable<{ approved: boolean; draftJobsEnqueued: number }> {
    return this.http.post<{ approved: boolean; draftJobsEnqueued: number }>(
      `/api/books/${id}/approve-outline`,
      {},
    );
  }

  updateOutline(id: string, chapters: OutlineChapterPayload[]): Observable<BookChapterSummary[]> {
    return this.http.put<BookChapterSummary[]>(
      `/api/books/${id}/outline`,
      { chapters },
    );
  }

  regenerateChapter(id: string, chapterId: string): Observable<{ requeued: boolean }> {
    return this.http.post<{ requeued: boolean }>(
      `/api/books/${id}/chapters/${chapterId}/regenerate`,
      {},
    );
  }

  listCharacters(id: string): Observable<BookCharacterDto[]> {
    return this.http.get<BookCharacterDto[]>(`/api/books/${id}/characters`);
  }

  createCharacter(id: string, req: CreateBookCharacterRequest): Observable<BookCharacterDto> {
    return this.http.post<BookCharacterDto>(`/api/books/${id}/characters`, req);
  }

  deleteCharacter(bookId: string, characterId: string): Observable<void> {
    return this.http.delete<void>(`/api/books/${bookId}/characters/${characterId}`);
  }

  listAssets(id: string): Observable<BookAsset[]> {
    return this.http.get<BookAsset[]>(`/api/books/${id}/assets`);
  }

  uploadAsset(id: string, file: File): Observable<BookAsset> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    return this.http.post<BookAsset>(`/api/books/${id}/assets/upload`, fd);
  }

  deleteAsset(id: string, assetId: string): Observable<void> {
    return this.http.delete<void>(`/api/books/${id}/assets/${assetId}`);
  }

  listExports(id: string): Observable<BookExportRow[]> {
    return this.http.get<BookExportRow[]>(`/api/books/${id}/exports`);
  }

  queueExports(id: string, formats: string[]): Observable<{ queued: string[] }> {
    return this.http.post<{ queued: string[] }>(`/api/books/${id}/exports`, { formats });
  }

  exportDownloadUrl(exportId: string): string {
    return `/api/exports/${exportId}/download`;
  }

  /**
   * Fetch a protected file as a Blob. Use this anywhere you'd otherwise put
   * the URL into `<a href>` — anchor tags can't carry the JWT, and on
   * production the SPA host and API host are different origins so a relative
   * `/api/...` href hits the SPA's 404 instead of the API. Going through
   * HttpClient routes the request through the auth interceptor (auth header +
   * apiBase prefix) and returns the bytes + headers for client-side download.
   */
  downloadAsBlob(url: string): Observable<HttpResponse<Blob>> {
    return this.http.get(url, { responseType: 'blob', observe: 'response' });
  }

  listCovers(id: string): Observable<BookCoverRow[]> {
    return this.http.get<BookCoverRow[]>(`/api/books/${id}/covers`);
  }

  searchCovers(id: string, query: string): Observable<UnsplashSearchResult[]> {
    return this.http.get<UnsplashSearchResult[]>(
      `/api/books/${id}/covers/search?q=${encodeURIComponent(query)}`);
  }

  selectCover(id: string, photoId: string, customQuery?: string): Observable<{
    id: string;
    assetId: string;
    assetUrl: string;
    attribution: unknown;
  }> {
    return this.http.post<{ id: string; assetId: string; assetUrl: string; attribution: unknown }>(
      `/api/books/${id}/covers/select`,
      { photoId, customQuery },
    );
  }

  generateAiCover(id: string, prompt?: string, style?: string): Observable<{
    id: string;
    assetId: string;
    status: string;
    downloadUrl: string;
    attribution: unknown;
  }> {
    return this.http.post<{
      id: string;
      assetId: string;
      status: string;
      downloadUrl: string;
      attribution: unknown;
    }>(`/api/books/${id}/covers/generate-ai`, { prompt, style });
  }

  deleteCover(id: string, coverId: string): Observable<void> {
    return this.http.delete<void>(`/api/books/${id}/covers/${coverId}`);
  }

  // The shape is intentionally loose — the editor UI is responsible for the
  // field-level schema. The server stores whatever JSON the front-end sends back.
  getPublisherMetadata(id: string): Observable<any> {
    return this.http.get<any>(`/api/books/${id}/publisher-metadata`);
  }

  putPublisherMetadata(id: string, body: any): Observable<any> {
    return this.http.put<any>(`/api/books/${id}/publisher-metadata`, body);
  }

  translateBook(id: string, language: string, style?: string): Observable<TranslateBookResponse> {
    const params: Record<string, string> = { language };
    if (style && style.trim()) params['style'] = style.trim();
    const qs = new URLSearchParams(params).toString();
    return this.http.post<TranslateBookResponse>(`/api/books/${id}/translate?${qs}`, {});
  }
}

export interface TranslatedChapterResult {
  assetId?: string;
  chapterId: string;
  chapterNumber: number;
  downloadUrl?: string;
  fileName?: string;
  provider?: string;
  sourceLanguage?: string;
  error?: string;
}

export interface TranslateBookResponse {
  projectId: string;
  targetLanguage: string;
  provider: string;
  chaptersTranslated: number;
  results: TranslatedChapterResult[];
}

export const STATUS_LABELS: Record<BookProjectStatus, string> = {
  0: 'Draft',
  1: 'In progress',
  2: 'Ready for review',
  3: 'Completed',
  4: 'Archived',
};

// User-facing workflow stage labels (book-level). Server-side stage strings for
// individual jobs ("Planning", "Drafting") go through HumanVoice instead.
export const STAGE_LABELS: Record<BookProjectWorkflowStage, string> = {
  0: 'Getting started',
  1: 'Planning the chapters',
  2: 'Writing the chapters',
  3: 'Polishing',
  4: 'Formatting',
  5: 'Publishing',
};

// User-facing labels for chapter status. Keep these in lockstep with HumanVoice on the
// server so the SPA never displays raw enum names like "Drafted".
export const CHAPTER_STATUS_LABELS: Record<BookChapterStatus, string> = {
  0: 'Outlined',
  1: 'Being written',
  2: 'Written',
  3: 'Being polished',
  4: 'Final',
};
