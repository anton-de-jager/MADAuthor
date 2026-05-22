import { Component, Input, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Subscription } from 'rxjs';
import {
  AuthorSummary,
  BookAsset,
  BookDetail,
  BookExportRow,
  BooksApi,
  CHAPTER_STATUS_LABELS,
  STAGE_LABELS,
  STATUS_LABELS,
  TranslatedChapterResult,
  UpdateBookRequest,
} from '../../core/api/books.api';
import { FormsModule } from '@angular/forms';
import { NotificationsService, JobProgressEvent } from '../../core/signalr/notifications.service';
import { CoverPickerComponent } from './cover-picker.component';
import { MetadataEditorComponent } from './metadata-editor.component';
import { OutlineEditorComponent } from './outline-editor.component';

@Component({
  selector: 'app-books-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, DecimalPipe, FormsModule, CoverPickerComponent, MetadataEditorComponent, OutlineEditorComponent],
  template: `
    <div class="p-8 max-w-5xl mx-auto">
      <a routerLink="/books" class="text-sm text-ink-400 hover:text-ink-100">&larr; All books</a>

      @if (loading()) {
        <div class="glass rounded-xl p-12 text-center text-ink-400 mt-6">Loading…</div>
      }
      @if (!loading() && book(); as b) {
        <div class="mt-3 mb-6 flex items-start justify-between gap-4">
          <div>
            <p class="text-xs uppercase tracking-wider text-brand-300">
              {{ stageLabel(b) }} · {{ statusLabel(b) }}
              @if (liveStage()) {
                <span class="ml-2 text-ink-400">· {{ liveStage() }}</span>
              }
            </p>
            <h1 class="font-display text-3xl font-semibold tracking-tight mt-1">{{ b.title }}</h1>
            @if (b.subtitle) {
              <p class="text-ink-400 mt-1">{{ b.subtitle }}</p>
            }
          </div>
          @if (b.chapters.length > 0) {
            <a [routerLink]="['/books', b.id, 'read']"
               class="text-sm bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 transition">
              Open reader →
            </a>
          }
        </div>

        <!-- Outline approval banner -->
        @if (b.requireOutlineApproval && !b.outlineApprovedAt && b.chapters.length > 0) {
          <div class="glass rounded-xl p-5 mb-6 border-amber-500/30 bg-amber-900/10">
            <div class="flex items-start justify-between gap-4 flex-wrap">
              <div class="min-w-0 flex-1">
                <div class="text-xs uppercase tracking-wider text-amber-300 mb-1">Outline ready for review</div>
                <p class="text-sm text-ink-200">
                  The Planner has produced {{ b.chapters.length }} chapters. Approve to start drafting,
                  or click Edit outline below to modify the chapter list.
                </p>
              </div>
              <div class="flex items-center gap-2">
                <button (click)="toggleOutlineEditor()" [disabled]="approving()"
                  class="border border-ink-700 hover:border-brand-500/50 text-ink-100 font-medium rounded-lg px-4 py-2 disabled:opacity-50 whitespace-nowrap text-sm">
                  {{ editingOutline() ? 'Close editor' : 'Edit outline' }}
                </button>
                <button (click)="approve()" [disabled]="approving() || editingOutline()"
                  class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 disabled:opacity-50 whitespace-nowrap"
                  [title]="editingOutline() ? 'Close the editor before approving.' : ''">
                  {{ approving() ? 'Approving…' : 'Approve outline' }}
                </button>
              </div>
            </div>
          </div>

          @if (editingOutline()) {
            <div class="mb-6">
              <app-outline-editor
                [projectId]="b.id"
                [chapters]="b.chapters"
                (saved)="onOutlineSaved()"
                (cancelled)="closeOutlineEditor()"></app-outline-editor>
            </div>
          }
        }

        <div class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
          <div class="glass rounded-xl p-4">
            <div class="text-xs uppercase tracking-wider text-ink-400">Author</div>
            <div class="mt-1">{{ b.authorPenName || '—' }}</div>
          </div>
          <div class="glass rounded-xl p-4">
            <div class="text-xs uppercase tracking-wider text-ink-400">Genre</div>
            <div class="mt-1">{{ b.genre || '—' }}</div>
          </div>
          <div class="glass rounded-xl p-4">
            <div class="text-xs uppercase tracking-wider text-ink-400">Audience</div>
            <div class="mt-1">{{ b.targetAudience || '—' }}</div>
          </div>
          <div class="glass rounded-xl p-4">
            <div class="text-xs uppercase tracking-wider text-ink-400">Created</div>
            <div class="mt-1">{{ b.createdDate | date: 'medium' }}</div>
          </div>
        </div>

        <!-- Edit details -->
        <div class="mb-8">
          <button (click)="toggleEditDetails()" class="text-sm text-brand-300 hover:text-brand-200 mb-3">
            {{ editingDetails() ? 'Cancel editing' : 'Edit details' }}
          </button>
          @if (editingDetails()) {
            <div class="glass rounded-xl p-5">
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Title</span>
                  <input [(ngModel)]="editForm.title" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Subtitle</span>
                  <input [(ngModel)]="editForm.subtitle" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Author</span>
                  <select [(ngModel)]="editForm.authorId" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50">
                    <option [ngValue]="null">— None —</option>
                    @for (a of authors(); track a.id) {
                      <option [ngValue]="a.id">{{ a.penName }}</option>
                    }
                  </select>
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Genre</span>
                  <input [(ngModel)]="editForm.genre" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Target audience</span>
                  <input [(ngModel)]="editForm.targetAudience" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Writing tone</span>
                  <input [(ngModel)]="editForm.writingTone" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Language</span>
                  <input [(ngModel)]="editForm.language" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Target word count</span>
                  <input type="number" [(ngModel)]="editForm.targetWordCount" [disabled]="saving()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
              </div>
              @if (saveError()) {
                <div class="text-sm text-rose-300 mt-3">{{ saveError() }}</div>
              }
              <div class="mt-4 flex gap-2">
                <button (click)="saveDetails()" [disabled]="saving()"
                  class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 text-sm disabled:opacity-50">
                  {{ saving() ? 'Saving…' : 'Save changes' }}
                </button>
                <button (click)="toggleEditDetails()" [disabled]="saving()"
                  class="border border-ink-700 hover:border-ink-500 text-ink-300 font-medium rounded-lg px-4 py-2 text-sm disabled:opacity-50">
                  Cancel
                </button>
              </div>
            </div>
          }
        </div>

        <!-- Progress -->
        <div class="glass rounded-xl p-6 mb-6">
          <div class="flex items-center justify-between mb-2">
            <div class="text-xs uppercase tracking-wider text-ink-400 flex items-center gap-2">
              Progress
              @if (live()) {
                <span class="inline-flex items-center gap-1 text-emerald-400">
                  <span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span>
                  live
                </span>
              }
            </div>
            <div class="text-sm text-ink-400">{{ effectiveProgress() }}%</div>
          </div>
          <div class="h-2 rounded-full bg-ink-800 overflow-hidden">
            <div class="h-full bg-gradient-to-r from-brand-500 to-fuchsia-500 transition-all duration-500"
                 [style.width.%]="effectiveProgress()"></div>
          </div>
          @if (lastError()) {
            <div class="mt-3 text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
              {{ lastError() }}
            </div>
          }
        </div>

        <!-- Chapters -->
        <h2 class="font-display text-xl mb-3">Chapters</h2>
        @if (b.chapters.length === 0) {
          <div class="glass rounded-xl p-8 text-center text-ink-400">
            No chapters yet. The Planner agent will populate them once the worker picks up the queued job.
          </div>
        } @else {
          <div class="space-y-2 mb-10">
            @for (ch of b.chapters; track ch.id) {
              <a [routerLink]="['/books', b.id, 'read']" [queryParams]="{ chapter: ch.id }"
                 class="glass rounded-lg p-4 flex items-center justify-between hover:border-brand-500/40 transition">
                <div>
                  <div class="text-xs text-ink-500">Chapter {{ ch.chapterNumber }}</div>
                  <div class="font-medium">{{ ch.title }}</div>
                  @if (ch.summary) {
                    <div class="text-sm text-ink-400 mt-1">{{ ch.summary }}</div>
                  }
                </div>
                <div class="text-right">
                  <div class="text-xs uppercase tracking-wider text-brand-300">{{ chapterStatus(ch.status) }}</div>
                  <div class="text-xs text-ink-500 mt-1">{{ ch.wordCount }} words</div>
                </div>
              </a>
            }
          </div>
        }

        <!-- Publisher metadata editor -->
        @if (showMetadataEditor(b)) {
          <div class="mb-10">
            <h2 class="font-display text-xl mb-3">Publishing details</h2>
            <div class="glass rounded-xl p-5">
              <app-metadata-editor [projectId]="b.id"></app-metadata-editor>
            </div>
          </div>
        }

        <!-- Cover picker -->
        <div class="mb-10">
          <app-cover-picker
            [projectId]="b.id"
            [bookTitle]="b.title"
            [bookGenre]="b.genre"
            [bookTone]="b.writingTone"></app-cover-picker>
        </div>

        <!-- Translation -->
        @if (showMetadataEditor(b)) {
          <div class="mb-10">
            <h2 class="font-display text-xl mb-3">Translate book</h2>
            <div class="glass rounded-xl p-5">
              <p class="text-sm text-ink-400 mb-4">
                Translate every Final chapter into another language. Each chapter is saved as a downloadable
                Markdown file attached to this project. Source language: <span class="text-ink-100">{{ b.language }}</span>.
              </p>
              <div class="grid grid-cols-1 md:grid-cols-2 gap-3 mb-3">
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Target language</span>
                  <select [value]="translateLanguage()" (change)="translateLanguage.set(toSelect($event))"
                    [disabled]="translating()"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50">
                    @for (opt of translateOptions; track opt.code) {
                      <option [value]="opt.code">{{ opt.label }}</option>
                    }
                  </select>
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Style hint (optional)</span>
                  <input [value]="translateStyle()" (input)="translateStyle.set(toInput($event))"
                    [disabled]="translating()"
                    placeholder="e.g. warm, conversational"
                    class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm text-ink-100 disabled:opacity-50" />
                </label>
              </div>
              <button (click)="translateBook()" [disabled]="translating() || translateDisabled()"
                class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 text-sm disabled:opacity-50">
                {{ translating() ? 'Translating chapters…' : 'Translate book' }}
              </button>
              @if (translating()) {
                <p class="text-xs text-ink-500 mt-2">This can take a few minutes per chapter — please keep this tab open.</p>
              }
              @if (translateError()) {
                <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2 mt-3">
                  {{ translateError() }}
                </div>
              }
              @if (translatedChapters().length > 0) {
                <div class="mt-4">
                  <div class="text-xs uppercase tracking-wider text-ink-400 mb-2">
                    Translated to {{ lastTranslatedLanguage() }} · via {{ lastTranslatedProvider() }}
                  </div>
                  <ul class="space-y-1.5 text-sm">
                    @for (r of translatedChapters(); track r.chapterId) {
                      <li class="flex items-center justify-between border border-ink-800 rounded-md px-3 py-2">
                        <div class="truncate">
                          <span class="text-ink-100">Chapter {{ r.chapterNumber }}</span>
                          @if (r.fileName) {
                            <span class="text-xs text-ink-500 ml-2">{{ r.fileName }}</span>
                          }
                          @if (r.error) {
                            <span class="block text-xs text-rose-300 mt-1">{{ r.error }}</span>
                          }
                        </div>
                        @if (r.downloadUrl) {
                          <button type="button" (click)="downloadFromApi(r.downloadUrl!, r.fileName)"
                            class="text-xs text-brand-300 hover:text-brand-200">Download</button>
                        }
                      </li>
                    }
                  </ul>
                </div>
              }
            </div>
          </div>
        }

        <!-- Uploads -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-10">
          <section>
            <h2 class="font-display text-xl mb-3">Source material</h2>
            <div class="glass rounded-xl p-5">
              <input #fileInput type="file" hidden (change)="onFile(fileInput)" />
              <button (click)="fileInput.click()" [disabled]="uploading()"
                class="w-full border border-dashed border-ink-700 hover:border-brand-500/50 rounded-lg px-4 py-6 text-sm text-ink-400 hover:text-ink-100 disabled:opacity-50 transition">
                {{ uploading() ? 'Uploading…' : '+ Upload a file (PDF / DOCX / TXT / image / audio · max 50 MB)' }}
              </button>
              @if (uploadError()) {
                <div class="text-sm text-rose-300 mt-2">{{ uploadError() }}</div>
              }
              @if (assets().length > 0) {
                <ul class="mt-4 space-y-1.5 text-sm">
                  @for (a of assets(); track a.id) {
                    <li class="flex items-center justify-between border border-ink-800 rounded-md px-3 py-2">
                      <div class="truncate">
                        <span class="text-ink-100">{{ a.fileName }}</span>
                        <span class="text-xs text-ink-500 ml-2">{{ a.mimeType }} · {{ a.fileSize | number }} B</span>
                      </div>
                      <button (click)="deleteAsset(a.id)" class="text-xs text-ink-400 hover:text-rose-300">Delete</button>
                    </li>
                  }
                </ul>
              }
            </div>
          </section>

          <!-- Exports -->
          <section>
            <h2 class="font-display text-xl mb-3">Exports</h2>
            <div class="glass rounded-xl p-5">
              <div class="flex flex-wrap gap-2 mb-4">
                <button (click)="queueExport('Pdf')" [disabled]="!canExport() || queueing()"
                  class="bg-brand-600 hover:bg-brand-500 text-white text-sm rounded px-3 py-1.5 disabled:opacity-40"
                  title="Screen-reading PDF">PDF</button>
                <button (click)="queueExport('Docx')" [disabled]="!canExport() || queueing()"
                  class="bg-brand-600 hover:bg-brand-500 text-white text-sm rounded px-3 py-1.5 disabled:opacity-40"
                  title="Microsoft Word DOCX">DOCX</button>
                <button (click)="queueExport('Epub')" [disabled]="!canExport() || queueing()"
                  class="bg-brand-600 hover:bg-brand-500 text-white text-sm rounded px-3 py-1.5 disabled:opacity-40"
                  title="Reflowable e-reader EPUB">EPUB</button>
                <button (click)="queueExport('Html')" [disabled]="!canExport() || queueing()"
                  class="bg-ink-800 hover:bg-ink-700 text-ink-100 text-sm rounded px-3 py-1.5 disabled:opacity-40 border border-ink-700"
                  title="Single-file styled HTML">HTML</button>
                <button (click)="queueExport('Markdown')" [disabled]="!canExport() || queueing()"
                  class="bg-ink-800 hover:bg-ink-700 text-ink-100 text-sm rounded px-3 py-1.5 disabled:opacity-40 border border-ink-700"
                  title="Plain-text Markdown with frontmatter">Markdown</button>
                <button (click)="queueExport('PrintPdfKdp')" [disabled]="!canExport() || queueing()"
                  class="bg-fuchsia-700 hover:bg-fuchsia-600 text-white text-sm rounded px-3 py-1.5 disabled:opacity-40"
                  title="Print-ready PDF for Amazon KDP (6×9, mirrored margins)">KDP Print</button>
                <button (click)="queueExport('PrintPdfIngram')" [disabled]="!canExport() || queueing()"
                  class="bg-fuchsia-700 hover:bg-fuchsia-600 text-white text-sm rounded px-3 py-1.5 disabled:opacity-40"
                  title="Print-ready PDF for IngramSpark (6×9, 0.125″ bleed)">Ingram Print</button>
              </div>
              @if (!canExport()) {
                <p class="text-xs text-ink-500">Exports unlock once at least one chapter has content.</p>
              }
              @if (exports().length > 0) {
                <ul class="space-y-1.5 text-sm">
                  @for (e of exports(); track e.id) {
                    <li class="flex items-center justify-between border border-ink-800 rounded-md px-3 py-2">
                      <div>
                        <span class="text-ink-100">{{ e.exportType }}</span>
                        <span class="text-xs ml-2"
                          [class.text-emerald-400]="e.status === 'Ready'"
                          [class.text-amber-300]="e.status === 'Queued' || e.status === 'Running'"
                          [class.text-rose-300]="e.status === 'Failed'">
                          {{ e.status }}
                        </span>
                        @if (e.errorMessage) {
                          <span class="block text-xs text-rose-300 mt-1">{{ e.errorMessage }}</span>
                        }
                      </div>
                      @if (e.status === 'Ready') {
                        <button type="button" (click)="downloadFromApi(downloadHref(e.id))"
                          class="text-xs text-brand-300 hover:text-brand-200">Download</button>
                      }
                    </li>
                  }
                </ul>
              }
            </div>
          </section>
        </div>
      }
    </div>
  `,
})
export class BooksDetailComponent implements OnInit, OnDestroy {
  @Input() id!: string;
  private api = inject(BooksApi);
  private notifications = inject(NotificationsService);

  loading = signal(true);
  book = signal<BookDetail | null>(null);
  assets = signal<BookAsset[]>([]);
  exports = signal<BookExportRow[]>([]);

  liveStage = signal<string | null>(null);
  liveProgress = signal<number | null>(null);
  live = signal(false);
  lastError = signal<string | null>(null);

  approving = signal(false);
  uploading = signal(false);
  uploadError = signal<string | null>(null);
  queueing = signal(false);
  editingOutline = signal(false);

  editingDetails = signal(false);
  saving = signal(false);
  saveError = signal<string | null>(null);
  authors = signal<AuthorSummary[]>([]);
  editForm: {
    title: string; subtitle: string; genre: string; targetAudience: string;
    writingTone: string; language: string; targetWordCount: number | null;
    authorId: string | null;
  } = { title: '', subtitle: '', genre: '', targetAudience: '', writingTone: '', language: 'en', targetWordCount: null, authorId: null };

  // Translation tab state. The 'none' code keeps the dropdown in a valid state when nothing
  // has been picked yet; the button stays disabled until the user chooses a real language.
  translating = signal(false);
  translateLanguage = signal<string>('Spanish');
  translateStyle = signal<string>('');
  translateError = signal<string | null>(null);
  translatedChapters = signal<TranslatedChapterResult[]>([]);
  lastTranslatedLanguage = signal<string | null>(null);
  lastTranslatedProvider = signal<string | null>(null);
  translateOptions = [
    { code: 'Spanish', label: 'Spanish' },
    { code: 'French', label: 'French' },
    { code: 'German', label: 'German' },
    { code: 'Portuguese', label: 'Portuguese' },
    { code: 'Italian', label: 'Italian' },
    { code: 'Dutch', label: 'Dutch' },
    { code: 'Polish', label: 'Polish' },
    { code: 'Turkish', label: 'Turkish' },
    { code: 'Arabic', label: 'Arabic' },
    { code: 'Japanese', label: 'Japanese' },
    { code: 'Korean', label: 'Korean' },
    { code: 'Mandarin Chinese', label: 'Mandarin Chinese' },
    { code: 'Afrikaans', label: 'Afrikaans' },
  ];

  effectiveProgress = computed(() => {
    const lp = this.liveProgress();
    return lp !== null ? lp : (this.book()?.completionPercentage ?? 0);
  });

  canExport = computed(() => (this.book()?.chapters ?? []).some((c) => c.wordCount > 0));

  // Disable the Translate button until at least one chapter is Final.
  translateDisabled = computed(() => !(this.book()?.chapters ?? []).some((c) => c.status === 4));

  private sub?: Subscription;
  private exportPoll?: ReturnType<typeof setInterval>;

  ngOnInit() {
    this.fetch();
    this.fetchAssets();
    this.fetchExports();

    this.notifications.joinProject(this.id).then(() => this.live.set(true));
    this.sub = this.notifications.jobProgress$.subscribe((evt: JobProgressEvent) => {
      if (evt.bookProjectId !== this.id) return;
      this.liveStage.set(evt.stage);
      this.liveProgress.set(evt.progress);
      this.lastError.set(evt.errorMessage);
      if (evt.status === 'Completed' || evt.status === 'Failed') {
        this.fetch();
      }
    });

    // Light polling for export status — exports go through Hangfire-style internal
    // service that doesn't push via SignalR yet.
    this.exportPoll = setInterval(() => this.fetchExports(), 4000);
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
    this.notifications.leaveProject(this.id);
    if (this.exportPoll) clearInterval(this.exportPoll);
  }

  private fetch() {
    this.api.get(this.id).subscribe({
      next: (b) => { this.book.set(b); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
  private fetchAssets() {
    this.api.listAssets(this.id).subscribe((list) => this.assets.set(list));
  }
  private fetchExports() {
    this.api.listExports(this.id).subscribe((list) => this.exports.set(list));
  }

  approve() {
    this.approving.set(true);
    this.api.approveOutline(this.id).subscribe({
      next: () => { this.approving.set(false); this.fetch(); },
      error: () => this.approving.set(false),
    });
  }

  toggleOutlineEditor() {
    this.editingOutline.update((v) => !v);
  }

  closeOutlineEditor() {
    this.editingOutline.set(false);
  }

  toggleEditDetails() {
    const next = !this.editingDetails();
    if (next) {
      const b = this.book();
      if (b) {
        this.editForm = {
          title: b.title,
          subtitle: b.subtitle ?? '',
          genre: b.genre ?? '',
          targetAudience: b.targetAudience ?? '',
          writingTone: b.writingTone ?? '',
          language: b.language,
          targetWordCount: b.targetWordCount ?? null,
          authorId: b.authorId ?? null,
        };
      }
      this.api.listAuthors().subscribe((a) => this.authors.set(a));
      this.saveError.set(null);
    }
    this.editingDetails.set(next);
  }

  saveDetails() {
    const b = this.book();
    if (!b) return;
    this.saving.set(true);
    this.saveError.set(null);
    const req: UpdateBookRequest = {};
    if (this.editForm.title !== b.title) req.title = this.editForm.title;
    if ((this.editForm.subtitle || null) !== (b.subtitle || null)) req.subtitle = this.editForm.subtitle || '';
    if ((this.editForm.genre || null) !== (b.genre || null)) req.genre = this.editForm.genre || '';
    if ((this.editForm.targetAudience || null) !== (b.targetAudience || null)) req.targetAudience = this.editForm.targetAudience || '';
    if ((this.editForm.writingTone || null) !== (b.writingTone || null)) req.writingTone = this.editForm.writingTone || '';
    if (this.editForm.language !== b.language) req.language = this.editForm.language;
    if (this.editForm.targetWordCount !== (b.targetWordCount ?? null)) req.targetWordCount = this.editForm.targetWordCount;
    if ((this.editForm.authorId || null) !== (b.authorId || null)) req.authorId = this.editForm.authorId;
    this.api.update(b.id, req).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.book.set(updated);
        this.editingDetails.set(false);
      },
      error: (err) => {
        this.saving.set(false);
        this.saveError.set(err?.error?.error ?? 'Save failed.');
      },
    });
  }

  // Refresh the book detail so the chapter list updates, then close the editor.
  onOutlineSaved() {
    this.fetch();
    this.editingOutline.set(false);
  }

  onFile(input: HTMLInputElement) {
    const file = input.files?.[0];
    if (!file) return;
    input.value = '';
    this.uploading.set(true);
    this.uploadError.set(null);
    this.api.uploadAsset(this.id, file).subscribe({
      next: () => { this.uploading.set(false); this.fetchAssets(); },
      error: (err) => {
        this.uploading.set(false);
        this.uploadError.set(err?.error?.error ?? 'Upload failed.');
      },
    });
  }

  deleteAsset(assetId: string) {
    this.api.deleteAsset(this.id, assetId).subscribe(() => this.fetchAssets());
  }

  queueExport(fmt: string) {
    this.queueing.set(true);
    this.api.queueExports(this.id, [fmt]).subscribe({
      next: () => { this.queueing.set(false); this.fetchExports(); },
      error: () => this.queueing.set(false),
    });
  }

  downloadHref(exportId: string) {
    return this.api.exportDownloadUrl(exportId);
  }

  /**
   * Trigger a file download for any auth-gated API URL. Uses HttpClient (which
   * the auth interceptor decorates with the bearer token + apiBase prefix),
   * then synthesizes an `<a download>` against a blob URL. Necessary because
   * raw `<a href>` against `/api/...` resolves against the SPA host on prod
   * and 404s, and even on the API host wouldn't include the JWT.
   */
  downloadFromApi(url: string, fallbackFileName?: string) {
    this.api.downloadAsBlob(url).subscribe({
      next: (resp) => {
        if (!resp.body) return;
        const blobUrl = URL.createObjectURL(resp.body);
        const cd = resp.headers.get('Content-Disposition') ?? '';
        // Honor RFC 5987 filename* if present (UTF-8 + percent-encoded),
        // else plain filename="…", else fall back to the caller's hint.
        let fileName = fallbackFileName ?? `download-${Date.now()}`;
        const star = /filename\*\s*=\s*(?:UTF-8'')?([^;\r\n]+)/i.exec(cd);
        if (star?.[1]) {
          try { fileName = decodeURIComponent(star[1].trim().replace(/^"|"$/g, '')); } catch { /* ignore */ }
        } else {
          const plain = /filename\s*=\s*"?([^";\r\n]+)"?/i.exec(cd);
          if (plain?.[1]) fileName = plain[1];
        }
        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        // Defer revoke so Safari/Firefox finish the navigation first.
        setTimeout(() => URL.revokeObjectURL(blobUrl), 1500);
      },
      error: (err) => {
        console.error('Download failed', err);
      },
    });
  }

  statusLabel(b: BookDetail) { return STATUS_LABELS[b.status] ?? b.status; }
  stageLabel(b: BookDetail) { return STAGE_LABELS[b.workflowStage] ?? b.workflowStage; }
  chapterStatus(s: number) { return CHAPTER_STATUS_LABELS[s as 0 | 1 | 2 | 3 | 4] ?? s; }

  // Hide the Publishing details editor while the book is still in Draft status
  // or Intake workflow stage — the Publisher agent hasn't run yet, so there
  // is no metadata JSON to edit.
  showMetadataEditor(b: BookDetail): boolean {
    return b.status !== 0 && b.workflowStage !== 0;
  }

  // Small template helpers for ChangeDetection-free signal bindings.
  toSelect(evt: Event): string { return (evt.target as HTMLSelectElement).value; }
  toInput(evt: Event): string { return (evt.target as HTMLInputElement).value; }

  translateBook() {
    const lang = this.translateLanguage().trim();
    if (!lang) return;
    this.translating.set(true);
    this.translateError.set(null);
    this.translatedChapters.set([]);
    this.api.translateBook(this.id, lang, this.translateStyle()).subscribe({
      next: (res) => {
        this.translating.set(false);
        this.translatedChapters.set(res.results ?? []);
        this.lastTranslatedLanguage.set(res.targetLanguage);
        this.lastTranslatedProvider.set(res.provider);
        if ((res.results ?? []).every((r) => !!r.error)) {
          this.translateError.set('No chapters were translated. Check the API logs for details.');
        }
      },
      error: (err) => {
        this.translating.set(false);
        const status = err?.status;
        const msg = err?.error?.error ?? err?.message ?? 'Translation failed.';
        if (status === 503) {
          this.translateError.set('No translation provider is configured. Set OPENAI_API_KEY or DEEPL_API_KEY on the API and restart.');
        } else {
          this.translateError.set(msg);
        }
      },
    });
  }
}
