import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { Component, Input, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  BookAsset,
  BookDetail,
  BookExportRow,
  BooksApi,
  TranslateBookResponse,
} from '../../core/api/books.api';
import { FormsModule } from '@angular/forms';
import { CoverPickerComponent } from './cover-picker.component';
import { MetadataEditorComponent } from './metadata-editor.component';
import { BookActionNavComponent } from './book-action-nav.component';
import { MadCloudTaskService, MadCloudWorkflowArea } from '../../core/api/madcloud-task.service';

type WorkflowArea = 'publishing' | 'assets' | 'covers' | 'metadata' | 'exports' | 'translate';

@Component({
  selector: 'app-book-workflow-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    DatePipe,
    DecimalPipe,
    CoverPickerComponent,
    MetadataEditorComponent,
    BookActionNavComponent,
  ],
  template: `
    <main class="p-8 max-w-6xl mx-auto">
      <a routerLink="/books" class="text-sm text-ink-400 hover:text-ink-100">&larr; All books</a>

      @if (book(); as b) {
        <header class="mt-4 mb-5 flex items-start justify-between gap-4">
          <div>
            <p class="text-xs uppercase tracking-wider text-brand-300">{{ areaTitle() }}</p>
            <h1 class="font-display text-3xl font-semibold tracking-tight">{{ b.title }}</h1>
            <p class="text-ink-400 mt-1">{{ areaBlurb() }}</p>
          </div>
          <button type="button" (click)="createTask()" [disabled]="taskBusy()"
            class="bg-brand-600 hover:bg-brand-500 disabled:opacity-50 text-white rounded-lg px-4 py-2 text-sm font-medium">
            {{ taskBusy() ? 'Creating...' : 'Create MADCloud task' }}
          </button>
        </header>

        <div class="mb-6">
          <app-book-action-nav [book]="b"></app-book-action-nav>
        </div>

        @if (taskMessage()) {
          <div class="mb-4 text-sm text-emerald-300 bg-emerald-900/20 border border-emerald-700/40 rounded-lg px-3 py-2">{{ taskMessage() }}</div>
        }

        @switch (area()) {
          @case ('metadata') {
            <section class="glass rounded-xl p-5"><app-metadata-editor [projectId]="b.id"></app-metadata-editor></section>
          }
          @case ('covers') {
            <app-cover-picker [projectId]="b.id" [bookTitle]="b.title" [bookSubtitle]="b.subtitle" [bookGenre]="b.genre" [bookTone]="b.writingTone" [bookAuthor]="b.authorPenName"></app-cover-picker>
          }
          @case ('assets') {
            <section class="glass rounded-xl p-5">
              <input #fileInput type="file" hidden (change)="upload(fileInput)" />
              <button type="button" (click)="fileInput.click()" [disabled]="uploading()"
                class="w-full border border-dashed border-ink-700 hover:border-brand-500/50 rounded-lg px-4 py-6 text-sm text-ink-400 hover:text-ink-100 disabled:opacity-50 transition">
                {{ uploading() ? 'Uploading...' : '+ Upload source material or generated asset' }}
              </button>
              @if (error()) { <p class="text-sm text-rose-300 mt-3">{{ error() }}</p> }
              <ul class="mt-5 space-y-2">
                @for (asset of assets(); track asset.id) {
                  <li class="border border-ink-800 rounded-lg px-4 py-3 flex items-center justify-between gap-3">
                    <div class="min-w-0">
                      <div class="text-ink-100 truncate">{{ asset.fileName }}</div>
                      <div class="text-xs text-ink-500">{{ asset.mimeType }} · {{ asset.fileSize | number }} B · {{ asset.createdDate | date:'medium' }}</div>
                    </div>
                    <button type="button" (click)="deleteAsset(asset.id)" class="text-xs text-ink-400 hover:text-rose-300">Delete</button>
                  </li>
                }
              </ul>
            </section>
          }
          @case ('exports') {
            <section class="glass rounded-xl p-5">
              <div class="flex flex-wrap gap-2 mb-5">
                @for (fmt of exportFormats; track fmt) {
                  <button type="button" (click)="queueExport(fmt)" [disabled]="queueing()"
                    class="bg-brand-600 hover:bg-brand-500 disabled:opacity-50 text-white text-sm rounded-lg px-3 py-2">{{ fmt }}</button>
                }
              </div>
              <ul class="space-y-2">
                @for (row of exports(); track row.id) {
                  <li class="border border-ink-800 rounded-lg px-4 py-3 flex items-center justify-between gap-3">
                    <div>
                      <span class="text-ink-100">{{ row.exportType }}</span>
                      <span class="text-xs ml-2" [class.text-emerald-300]="row.status === 'Ready'" [class.text-amber-300]="row.status !== 'Ready'">{{ row.status }}</span>
                      @if (row.errorMessage) { <div class="text-xs text-rose-300 mt-1">{{ row.errorMessage }}</div> }
                    </div>
                    <div class="flex gap-3">
                      @if (row.status === 'Ready') {
                        <button type="button" (click)="download(row)" class="text-xs text-brand-300 hover:text-brand-200">Download</button>
                      }
                      <button type="button" (click)="deleteExport(row.id)" class="text-xs text-ink-400 hover:text-rose-300">Delete</button>
                    </div>
                  </li>
                }
              </ul>
            </section>
          }
          @case ('translate') {
            <section class="glass rounded-xl p-5">
              <p class="text-sm text-ink-400 mb-4">Translation is routed through MADCloud. You can create a MADCloud task, or try the current translation endpoint if a MADCloud-backed translator is configured.</p>
              <div class="grid grid-cols-1 md:grid-cols-2 gap-3 mb-4">
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Target language</span>
                  <select [(ngModel)]="language" class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm">
                    <option value="es">Spanish</option><option value="fr">French</option><option value="de">German</option><option value="pt">Portuguese</option><option value="af">Afrikaans</option>
                  </select>
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Style hint</span>
                  <input [(ngModel)]="style" class="mt-1 w-full bg-ink-900 border border-ink-700 rounded-md px-3 py-2 text-sm" placeholder="warm, clear, publication-ready" />
                </label>
              </div>
              <div class="flex flex-wrap gap-2">
                <button type="button" (click)="createTask()" class="bg-brand-600 hover:bg-brand-500 text-white rounded-lg px-4 py-2 text-sm">Create MADCloud translation task</button>
                <button type="button" (click)="translate(b.id)" [disabled]="translating()" class="border border-ink-700 hover:border-ink-500 rounded-lg px-4 py-2 text-sm">{{ translating() ? 'Translating...' : 'Try translation endpoint' }}</button>
              </div>
              @if (translationResult()) { <pre class="mt-4 bg-ink-950 rounded-lg p-3 text-xs overflow-auto">{{ translationResult() }}</pre> }
            </section>
          }
          @default {
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <section class="glass rounded-xl p-5"><h2 class="font-display text-xl mb-3">Publishing metadata</h2><app-metadata-editor [projectId]="b.id"></app-metadata-editor></section>
              <section class="glass rounded-xl p-5">
                <h2 class="font-display text-xl mb-3">Export readiness</h2>
                <p class="text-sm text-ink-400 mb-4">Use focused pages for cover, asset, export, and translation workflows.</p>
                <div class="flex flex-wrap gap-2">
                  <a [routerLink]="['/books', b.id, 'covers']" class="border border-ink-700 rounded-lg px-3 py-2 text-sm">Covers</a>
                  <a [routerLink]="['/books', b.id, 'exports']" class="border border-ink-700 rounded-lg px-3 py-2 text-sm">Exports</a>
                  <a [routerLink]="['/books', b.id, 'assets']" class="border border-ink-700 rounded-lg px-3 py-2 text-sm">Assets</a>
                  <a [routerLink]="['/books', b.id, 'translate']" class="border border-ink-700 rounded-lg px-3 py-2 text-sm">Translation</a>
                </div>
              </section>
            </div>
          }
        }
      } @else {
        <div class="glass rounded-xl p-12 text-center text-ink-400 mt-6">Loading...</div>
      }
    </main>
  `,
})
export class BookWorkflowPageComponent implements OnInit {
  @Input({ required: true }) id!: string;

  book = signal<BookDetail | null>(null);
  assets = signal<BookAsset[]>([]);
  exports = signal<BookExportRow[]>([]);
  error = signal('');
  taskBusy = signal(false);
  taskMessage = signal('');
  uploading = signal(false);
  queueing = signal(false);
  translating = signal(false);
  translationResult = signal('');
  language = 'es';
  style = '';
  exportFormats = ['Pdf', 'Docx', 'Epub', 'Html', 'Markdown', 'PrintPdfKdp', 'PrintPdfIngram'];

  area = computed<WorkflowArea>(() => (this.route.snapshot.data['area'] ?? 'publishing') as WorkflowArea);
  areaTitle = computed(() => ({
    publishing: 'Publishing',
    assets: 'Source material',
    covers: 'Cover designer',
    metadata: 'Publishing metadata',
    exports: 'Exports',
    translate: 'Translation',
  })[this.area()]);
  areaBlurb = computed(() => ({
    publishing: 'Metadata, covers, exports, assets, and translations are grouped here.',
    assets: 'Upload, inspect, and remove source material or generated assets.',
    covers: 'Select source imagery, preview templates, and create print wraps.',
    metadata: 'Review KDP copy, keywords, front matter, author bio, and placeholders.',
    exports: 'Queue, download, and delete publishing files.',
    translate: 'Route translation work through MADCloud.',
  })[this.area()]);

  constructor(
    private route: ActivatedRoute,
    private api: BooksApi,
    private madcloud: MadCloudTaskService,
  ) {}

  ngOnInit() {
    this.load();
  }

  load() {
    this.api.get(this.id).subscribe((book) => {
      this.book.set(book);
      if (this.area() === 'assets') this.fetchAssets();
      if (this.area() === 'exports') this.fetchExports();
    });
  }

  createTask() {
    const book = this.book();
    if (!book || this.taskBusy()) return;
    const area = this.toMadCloudArea();
    this.taskBusy.set(true);
    this.taskMessage.set('');
    this.madcloud.createBookTask(book, area, `Create MADCloud work for ${book.title}. Workflow area: ${this.areaTitle()}.`).subscribe({
      next: () => { this.taskBusy.set(false); this.taskMessage.set('MADCloud task created.'); },
      error: () => { this.taskBusy.set(false); this.taskMessage.set('Could not create MADCloud task.'); },
    });
  }

  upload(input: HTMLInputElement) {
    const file = input.files?.[0];
    if (!file) return;
    this.uploading.set(true);
    this.error.set('');
    this.api.uploadAsset(this.id, file).subscribe({
      next: () => { this.uploading.set(false); input.value = ''; this.fetchAssets(); },
      error: (err) => { this.uploading.set(false); this.error.set(err?.error?.error ?? 'Upload failed.'); },
    });
  }

  fetchAssets() { this.api.listAssets(this.id).subscribe((rows) => this.assets.set(rows)); }
  deleteAsset(assetId: string) { this.api.deleteAsset(this.id, assetId).subscribe(() => this.fetchAssets()); }
  fetchExports() { this.api.listExports(this.id).subscribe((rows) => this.exports.set(rows)); }
  queueExport(fmt: string) {
    this.queueing.set(true);
    this.api.queueExports(this.id, [fmt]).subscribe({ next: () => { this.queueing.set(false); this.fetchExports(); }, error: () => this.queueing.set(false) });
  }
  deleteExport(exportId: string) { this.api.deleteExport(exportId).subscribe(() => this.fetchExports()); }
  download(row: BookExportRow) {
    this.api.downloadAsBlob(this.api.exportDownloadUrl(row.id)).subscribe((resp) => {
      if (!resp.body) return;
      const url = URL.createObjectURL(resp.body);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${row.exportType.toLowerCase()}-${row.id}`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    });
  }
  translate(bookId: string) {
    this.translating.set(true);
    this.translationResult.set('');
    this.api.translateBook(bookId, this.language, this.style).subscribe({
      next: (res: TranslateBookResponse) => { this.translating.set(false); this.translationResult.set(JSON.stringify(res, null, 2)); },
      error: (err) => { this.translating.set(false); this.translationResult.set(err?.error?.error ?? 'Translation unavailable.'); },
    });
  }

  private toMadCloudArea(): MadCloudWorkflowArea {
    const area = this.area();
    if (area === 'covers') return 'Cover';
    if (area === 'translate') return 'Translation';
    if (area === 'metadata' || area === 'publishing') return 'Metadata';
    return 'General';
  }
}
