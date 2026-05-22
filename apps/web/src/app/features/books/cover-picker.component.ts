import { Component, Input, OnChanges, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  BooksApi,
  BookCoverRow,
  UnsplashSearchResult,
} from '../../core/api/books.api';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-cover-picker',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <section class="glass rounded-xl p-5">
      <div class="flex items-center justify-between mb-3">
        <h2 class="font-display text-xl">Cover</h2>
        <span class="text-[10px] uppercase tracking-wider text-ink-500">
          Royalty-free via Unsplash
        </span>
      </div>

      <!-- Currently selected -->
      @if (selectedCover(); as sel) {
        <div class="flex gap-4 items-start mb-4">
          @if (sel.assetUrl) {
            <img [src]="absoluteUrl(sel.assetUrl)" alt="Selected cover"
              class="w-32 h-44 object-cover rounded-md border border-ink-700" />
          }
          <div class="flex-1 text-sm">
            <div class="text-xs uppercase tracking-wider text-emerald-300 mb-1">Selected</div>
            <div class="text-ink-200">{{ sel.prompt }}</div>
            <div class="text-xs text-ink-500 mt-1">{{ sel.style }}</div>
            @if (attributionLine(sel); as attr) {
              <div class="text-xs text-ink-400 mt-2">
                {{ attr.text }}
                @if (attr.url) {
                  <a [href]="attr.url" target="_blank" rel="noopener" class="text-brand-300 hover:text-brand-200 ml-1">↗</a>
                }
              </div>
            }
          </div>
        </div>
      }

      <!-- Search -->
      <form (submit)="$event.preventDefault(); search()" class="flex gap-2 mb-3">
        <input
          type="text"
          [formControl]="query"
          [placeholder]="defaultQuery() || 'e.g. quiet mountain morning, dusk shoreline, vintage typewriter'"
          class="flex-1 bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm" />
        <button type="submit" [disabled]="searching() || (!query.value && !defaultQuery())"
          class="bg-brand-600 hover:bg-brand-500 disabled:opacity-50 text-white text-sm rounded px-4 py-2">
          {{ searching() ? 'Searching…' : 'Search' }}
        </button>
      </form>

      <!-- Generate with AI -->
      <div class="border border-ink-700/60 rounded-lg p-3 mb-3 bg-ink-900/30">
        <div class="flex items-center justify-between mb-2">
          <div class="text-xs uppercase tracking-wider text-ink-400">Generate with AI</div>
          <span class="text-[10px] text-ink-500">OpenAI / Stability</span>
        </div>
        <div class="flex flex-col md:flex-row gap-2">
          <input
            type="text"
            [formControl]="aiPrompt"
            placeholder="leave blank to auto-generate from book details"
            class="flex-1 bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm" />
          <select
            [formControl]="aiStyle"
            class="bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm md:w-44">
            <option value="">Default style</option>
            <option value="cinematic">cinematic</option>
            <option value="minimalist">minimalist</option>
            <option value="oil painting">oil painting</option>
            <option value="photographic">photographic</option>
            <option value="illustration">illustration</option>
            <option value="vintage book cover">vintage book cover</option>
          </select>
          <button type="button"
            (click)="generateAi()"
            [disabled]="generatingAi()"
            class="bg-brand-600 hover:bg-brand-500 disabled:opacity-50 text-white text-sm rounded px-4 py-2 md:w-40">
            {{ generatingAi() ? 'Generating…' : 'Generate with AI' }}
          </button>
        </div>
      </div>

      @if (errorMessage()) {
        <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2 mb-3">
          {{ errorMessage() }}
          @if (notConfigured()) {
            <div class="text-xs text-ink-400 mt-1">
              Add <code class="text-brand-300">UNSPLASH_ACCESS_KEY</code> to your <code class="text-brand-300">.env</code>
              and restart the API. Free Access Key at
              <a href="https://unsplash.com/developers" target="_blank" rel="noopener"
                 class="text-brand-300 hover:text-brand-200">unsplash.com/developers</a>.
            </div>
          }
          @if (aiNotConfigured()) {
            <div class="text-xs text-ink-400 mt-1">
              No AI provider configured. Set
              <code class="text-brand-300">OPENAI_API_KEY</code>
              or <code class="text-brand-300">STABILITY_API_KEY</code>
              in your <code class="text-brand-300">.env</code> and restart the API.
            </div>
          }
        </div>
      }

      <!-- Results grid -->
      @if (results().length > 0) {
        <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
          @for (r of results(); track r.id) {
            <button type="button"
              (click)="select(r)"
              [disabled]="selecting() !== null"
              class="group relative rounded-md overflow-hidden border border-ink-700 hover:border-brand-500/50 transition disabled:opacity-50"
              [style.aspect-ratio]="'2/3'"
              [style.background-color]="r.color || '#1e293b'">
              <img [src]="r.thumbUrl" [alt]="r.altDescription || 'Unsplash photo'"
                loading="lazy"
                class="w-full h-full object-cover" />
              <div class="absolute inset-x-0 bottom-0 bg-gradient-to-t from-ink-950/90 via-ink-950/40 to-transparent p-2 text-left">
                <div class="text-[10px] text-ink-200 truncate">
                  by {{ r.photographer.name }}
                </div>
              </div>
              @if (selecting() === r.id) {
                <div class="absolute inset-0 grid place-items-center bg-ink-950/70 text-xs text-ink-100">
                  Saving…
                </div>
              }
            </button>
          }
        </div>
      } @else if (!searching() && hasSearched()) {
        <div class="text-sm text-ink-400">No results. Try a different query.</div>
      }
    </section>
  `,
})
export class CoverPickerComponent implements OnInit, OnChanges {
  @Input() projectId!: string;
  @Input() bookTitle?: string | null;
  @Input() bookGenre?: string | null;
  @Input() bookTone?: string | null;

  private api = inject(BooksApi);

  query = new FormControl<string>('', { nonNullable: true, validators: [Validators.maxLength(120)] });
  aiPrompt = new FormControl<string>('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  aiStyle = new FormControl<string>('', { nonNullable: true });
  results = signal<UnsplashSearchResult[]>([]);
  covers = signal<BookCoverRow[]>([]);
  searching = signal(false);
  selecting = signal<string | null>(null);
  generatingAi = signal(false);
  hasSearched = signal(false);
  errorMessage = signal<string | null>(null);
  notConfigured = signal(false);
  aiNotConfigured = signal(false);

  selectedCover = computed(() => this.covers().find((c) => c.status === 'Selected') ?? null);

  /**
   * Server returns relative asset URLs like `/api/books/{id}/assets/{aid}/download`.
   * `<img src>` doesn't go through the auth interceptor, so a relative URL resolves
   * against the SPA host (madauthor.madproducts.co.za) which has no /api — 404.
   * Prepend the API host explicitly. In dev `environment.apiBase` is empty so it
   * stays relative and the ng-serve proxy handles it.
   */
  absoluteUrl(url: string): string {
    if (!url) return url;
    if (url.startsWith('http://') || url.startsWith('https://')) return url;
    return (environment.apiBase || '') + url;
  }

  defaultQuery = computed(() => {
    const parts: string[] = [];
    if (this.bookTone) parts.push(this.bookTone);
    if (this.bookGenre) parts.push(this.bookGenre);
    if (this.bookTitle) parts.push(this.bookTitle);
    return parts.filter(Boolean).slice(0, 3).join(' ').trim();
  });

  ngOnInit() {
    this.fetchCovers();
  }

  ngOnChanges() {
    if (this.projectId && this.covers().length === 0) this.fetchCovers();
  }

  fetchCovers() {
    if (!this.projectId) return;
    this.api.listCovers(this.projectId).subscribe((list) => this.covers.set(list));
  }

  search() {
    const q = (this.query.value || this.defaultQuery() || '').trim();
    if (!q) return;
    this.searching.set(true);
    this.errorMessage.set(null);
    this.notConfigured.set(false);
    this.api.searchCovers(this.projectId, q).subscribe({
      next: (rows) => {
        this.results.set(rows);
        this.searching.set(false);
        this.hasSearched.set(true);
      },
      error: (err) => {
        this.searching.set(false);
        const msg = err?.error?.error ?? 'Search failed.';
        this.errorMessage.set(msg);
        if (err?.status === 503) this.notConfigured.set(true);
      },
    });
  }

  select(r: UnsplashSearchResult) {
    this.selecting.set(r.id);
    this.api.selectCover(this.projectId, r.id, this.query.value || undefined).subscribe({
      next: () => {
        this.selecting.set(null);
        this.fetchCovers();
      },
      error: (err) => {
        this.selecting.set(null);
        this.errorMessage.set(err?.error?.error ?? 'Could not save that cover.');
      },
    });
  }

  generateAi() {
    if (!this.projectId || this.generatingAi()) return;
    this.generatingAi.set(true);
    this.errorMessage.set(null);
    this.notConfigured.set(false);
    this.aiNotConfigured.set(false);
    const prompt = (this.aiPrompt.value || '').trim() || undefined;
    const style = (this.aiStyle.value || '').trim() || undefined;
    this.api.generateAiCover(this.projectId, prompt, style).subscribe({
      next: () => {
        this.generatingAi.set(false);
        this.fetchCovers();
      },
      error: (err) => {
        this.generatingAi.set(false);
        const msg = err?.error?.error ?? 'AI cover generation failed.';
        this.errorMessage.set(msg);
        if (err?.status === 503) this.aiNotConfigured.set(true);
      },
    });
  }

  attributionLine(c: BookCoverRow): { text: string; url: string | null } | null {
    if (!c.attribution) return null;
    try {
      const a = JSON.parse(c.attribution);
      const text = a.source ? `Cover photo by ${a.name} on ${a.source}` : `Cover photo by ${a.name}`;
      return { text, url: a.photoUrl ?? a.url ?? null };
    } catch {
      return null;
    }
  }
}
