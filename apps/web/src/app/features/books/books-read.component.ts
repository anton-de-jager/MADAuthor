import {
  AfterViewInit,
  Component,
  ElementRef,
  HostListener,
  Input,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { marked } from 'marked';
import { BooksApi, BookChapterDetail, BookDetail } from '../../core/api/books.api';

@Component({
  selector: 'app-books-read',
  standalone: true,
  imports: [RouterLink],
  template: `
    <!-- Fills the shell's main area. h-full + overflow-hidden tells the browser
         to constrain to parent height, suppress page scroll, and let the children
         (sidebar nav, reading pane) own their own layout. -->
    <div class="h-full flex bg-ink-950 text-ink-100 overflow-hidden">

      <!-- Chapter sidebar: fixed header at top, scrolling list below -->
      <aside class="hidden md:flex w-72 flex-col border-r border-ink-800/70 bg-ink-900/40 backdrop-blur">
        <div class="px-5 py-4 border-b border-ink-800/70 flex-none">
          <a [routerLink]="['/books', id]" class="text-xs text-ink-400 hover:text-ink-100">&larr; Back</a>
          @if (book(); as b) {
            <h1 class="font-display text-lg font-semibold mt-1 leading-tight">{{ b.title }}</h1>
            @if (b.subtitle) {
              <p class="text-xs text-ink-400 mt-0.5">{{ b.subtitle }}</p>
            }
          }
        </div>
        <nav class="flex-1 min-h-0 overflow-y-auto px-2 py-3 space-y-0.5 text-sm">
          @for (ch of chapters(); track ch.id) {
            <button type="button"
              (click)="selectChapter(ch.id)"
              [class]="
                'w-full text-left px-3 py-2 rounded-md border ' +
                (selectedId() === ch.id
                  ? 'border-brand-500/40 bg-brand-600/15 text-brand-200'
                  : 'border-transparent text-ink-300 hover:text-ink-100 hover:bg-ink-800/40')
              ">
              <div class="text-[10px] uppercase tracking-wider text-ink-500">Ch {{ ch.chapterNumber }}</div>
              <div class="leading-tight">{{ ch.title }}</div>
            </button>
          }
        </nav>
      </aside>

      <!-- Reading pane: fixed top bar, paged viewer, fixed bottom Prev/Next bar -->
      <section class="flex-1 min-w-0 flex flex-col">

        <!-- Top bar (sticky, never scrolls) -->
        <header class="flex items-center gap-3 text-xs text-ink-400 px-6 md:px-12 py-4 border-b border-ink-800/70 bg-ink-900/30 backdrop-blur flex-none">
          <span>Chapter {{ current()?.chapterNumber }}</span>
          <span class="opacity-50">·</span>
          <span>{{ current()?.wordCount ?? 0 }} words</span>
          <span class="opacity-50">·</span>
          <span class="uppercase tracking-wider text-brand-300">{{ current()?.status }}</span>
          <div class="ml-auto flex items-center gap-3">
            <span class="text-ink-500 tabular-nums">Page {{ pageIndex() + 1 }} / {{ pageCount() }}</span>
            <button (click)="regenerate()" [disabled]="regenerating()"
              class="px-3 py-1 text-xs border border-brand-500/50 text-brand-200 rounded hover:bg-brand-600/15 disabled:opacity-50">
              {{ regenerating() ? 'Queuing…' : 'Regenerate' }}
            </button>
          </div>
        </header>

        <!-- Paged viewer. flex-1 + min-h-0 so it owns its own box; overflow-hidden
             prevents any spillover from the CSS-columns layout inside. -->
        <div #viewport class="flex-1 min-h-0 relative overflow-hidden book-viewport"
             [class.flipping-next]="flipDirection() === 'next'"
             [class.flipping-prev]="flipDirection() === 'prev'">
          @if (loading()) {
            <div class="absolute inset-0 flex items-center justify-center text-ink-400">Loading…</div>
          }
          @if (!loading() && current(); as ch) {
            @if (ch.contentMarkdown) {
              <!-- The page-stage is sized to one page; the column-track inside is
                   the full multi-column flow, translated horizontally to reveal
                   the active page. -->
              <div class="page-stage" [style.width.px]="pageWidth()" [style.height.px]="pageHeight()">
                <div #track class="column-track reader"
                  [style.width.px]="pageWidth()"
                  [style.height.px]="pageHeight()"
                  [style.columnWidth.px]="pageWidth()"
                  [style.columnGap.px]="columnGap"
                  [style.fontFamily]="bodyFontStack()"
                  [style.transform]="'translateX(' + (-pageIndex() * (pageWidth() + columnGap)) + 'px)'"
                  [innerHTML]="renderedHtml()">
                </div>
              </div>
            } @else {
              <div class="absolute inset-0 flex flex-col items-center justify-center text-ink-400">
                <p class="mb-2">This chapter hasn't been drafted yet.</p>
                <p class="text-xs">Our writer will get to it shortly.</p>
              </div>
            }
          }
        </div>

        <!-- Footer bar (sticky, never scrolls) -->
        <footer class="flex items-center justify-between px-6 md:px-12 py-3 border-t border-ink-800/70 bg-ink-900/30 backdrop-blur flex-none">
          <button (click)="prevPage()" [disabled]="!hasPrevPage()"
            class="text-sm px-3 py-2 rounded-md border border-ink-700 hover:border-ink-500 disabled:opacity-30">
            &larr; Previous page
          </button>
          <div class="text-xs text-ink-500">
            @if (current(); as c) {
              Chapter {{ c.chapterNumber }} / {{ chapters().length }} · Page {{ pageIndex() + 1 }} / {{ pageCount() }}
            }
          </div>
          <button (click)="nextPage()" [disabled]="!hasNextPage()"
            class="text-sm px-3 py-2 rounded-md border border-ink-700 hover:border-ink-500 disabled:opacity-30">
            Next page &rarr;
          </button>
        </footer>
      </section>
    </div>
  `,
  styles: [
    `
    /* The viewport contains the page-stage centred horizontally. We use 3D
       perspective on the viewport so child rotateY values produce depth. */
    .book-viewport {
      perspective: 2200px;
      perspective-origin: 50% 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 1.5rem 1rem;
      background:
        radial-gradient(circle at 50% 0%, rgba(124, 58, 237, 0.05), transparent 60%),
        #0b1220;
    }
    .page-stage {
      position: relative;
      max-width: 100%;
      max-height: 100%;
      transform-style: preserve-3d;
    }
    /* The column-track holds the full multi-column flow. We translate it on X
       to reveal the active page; the parent stage has overflow:hidden so the
       other columns are clipped. The transition produces the slide. */
    .column-track {
      column-count: 1;
      column-fill: auto;
      column-rule: none;
      transition: transform 620ms cubic-bezier(0.7, 0, 0.2, 1);
      will-change: transform;
    }
    /* When user clicks next/prev we briefly add a class that applies a slight
       3D tilt on the outgoing edge so the slide reads as a page flip rather
       than a panel scroll. The class is removed after the transition. */
    .book-viewport.flipping-next .page-stage {
      animation: page-flip-next 620ms cubic-bezier(0.7, 0, 0.2, 1);
    }
    .book-viewport.flipping-prev .page-stage {
      animation: page-flip-prev 620ms cubic-bezier(0.7, 0, 0.2, 1);
    }
    @keyframes page-flip-next {
      0%   { transform: rotateY(0deg) translateZ(0); box-shadow: 0 0 0 rgba(0,0,0,0); }
      45%  { transform: rotateY(-6deg) translateZ(40px); box-shadow: -40px 0 60px -20px rgba(0,0,0,0.55); }
      100% { transform: rotateY(0deg) translateZ(0); box-shadow: 0 0 0 rgba(0,0,0,0); }
    }
    @keyframes page-flip-prev {
      0%   { transform: rotateY(0deg) translateZ(0); box-shadow: 0 0 0 rgba(0,0,0,0); }
      45%  { transform: rotateY(6deg) translateZ(40px); box-shadow: 40px 0 60px -20px rgba(0,0,0,0.55); }
      100% { transform: rotateY(0deg) translateZ(0); box-shadow: 0 0 0 rgba(0,0,0,0); }
    }

    /* ---- typography: print-styled, justified, hyphenated ---- */
    /* font-family is set as an inline style on the .reader element so the
       book's chosen BodyFont (BookProject.BodyFont) overrides the default
       serif stack. The fallback stack below is what gets used when no font
       is selected — matches the export renderers' Georgia fallback. */
    .reader {
      font-family: 'Crimson Pro', 'Source Serif Pro', Georgia, 'Times New Roman', serif;
      font-size: 1.0625rem;
      line-height: 1.55;
      color: #e2e8f0;
      text-align: justify;
      hyphens: auto;
      -webkit-hyphens: auto;
      -ms-hyphens: auto;
      padding: 0 0.25rem;
    }
    /* Block paragraphs (no indent, small vertical space between) — matches the
       export renderers' new layout. Paragraphs immediately after headings or
       quotes don't get the top margin because the heading/quote already supplies
       it; this keeps the rhythm tight at section boundaries. */
    .reader :where(p) {
      margin: 0 0 0.6em 0;
      text-indent: 0;
      orphans: 2;
      widows: 2;
    }
    .reader :where(p:last-child) { margin-bottom: 0; }
    .reader :where(h1 + p),
    .reader :where(h2 + p),
    .reader :where(h3 + p),
    .reader :where(blockquote + p) {
      margin-top: 0;
    }
    .reader :where(h1) {
      font-family: 'Space Grotesk', Inter, sans-serif;
      font-weight: 700;
      font-size: 2rem;
      line-height: 1.15;
      color: #f8fafc;
      text-align: left;
      margin: 0 0 1.5em;
      text-indent: 0;
      letter-spacing: -0.01em;
      break-after: avoid-column;
    }
    .reader :where(h2) {
      font-family: 'Space Grotesk', Inter, sans-serif;
      font-weight: 700;
      font-size: 1.35rem;
      line-height: 1.25;
      color: #f1f5f9;
      text-align: left;
      margin: 1.5em 0 0.75em;
      text-indent: 0;
      break-after: avoid-column;
    }
    .reader :where(h3) {
      font-family: 'Space Grotesk', Inter, sans-serif;
      font-weight: 700;
      font-size: 1.1rem;
      line-height: 1.3;
      color: #f1f5f9;
      text-align: left;
      margin: 1em 0 0.5em;
      text-indent: 0;
      break-after: avoid-column;
    }
    .reader :where(blockquote) {
      text-indent: 0;
      border-left: 3px solid #6d28d9;
      padding-left: 1rem;
      margin: 1em 0;
      color: #cbd5e1;
      font-style: italic;
    }
    .reader :where(blockquote p) { text-indent: 0; }
    .reader :where(ul, ol) {
      text-indent: 0;
      margin: 0.75em 0 0.75em 1.5em;
      padding: 0;
    }
    .reader :where(li) { text-indent: 0; margin: 0.15em 0; }
    .reader :where(code) {
      background: #1e293b;
      padding: 0.05rem 0.3rem;
      border-radius: 3px;
      font-family: Consolas, monospace;
      font-size: 0.88em;
    }
    .reader :where(em) { font-style: italic; }
    .reader :where(strong) { font-weight: 600; color: #f1f5f9; }
    `,
  ],
})
export class BooksReadComponent implements OnInit, AfterViewInit, OnDestroy {
  @Input() id!: string;
  private api = inject(BooksApi);

  // Constants. columnGap is the visual gutter between adjacent pages - it
  // matches the CSS column-gap and is included in the per-page step distance.
  readonly columnGap = 64;
  private readonly minPageWidthPx = 320;
  private readonly maxPageWidthPx = 640;
  private readonly horizontalPaddingPx = 32; // viewport padding on each side
  private readonly verticalPaddingPx = 48;

  loading = signal(true);
  book = signal<BookDetail | null>(null);
  selectedId = signal<string | null>(null);
  cachedChapters = signal<Record<string, BookChapterDetail>>({});
  regenerating = signal(false);

  // Pagination state
  pageIndex = signal(0);
  pageCount = signal(1);
  pageWidth = signal(560);
  pageHeight = signal(720);
  flipDirection = signal<'next' | 'prev' | null>(null);

  // Element refs. The viewport is the sizing reference; the track is the
  // multi-column flow whose scrollWidth tells us how many pages exist.
  viewport = viewChild<ElementRef<HTMLDivElement>>('viewport');
  track = viewChild<ElementRef<HTMLDivElement>>('track');

  private resizeObserver: ResizeObserver | null = null;
  private flipTimer: number | null = null;

  chapters = computed(() => this.book()?.chapters ?? []);
  current = computed<BookChapterDetail | null>(() => {
    const id = this.selectedId();
    if (!id) return null;
    return this.cachedChapters()[id] ?? null;
  });

  renderedHtml = computed(() => {
    const md = this.current()?.contentMarkdown ?? '';
    if (!md) return '';
    return marked.parse(md, { breaks: false, async: false }) as string;
  });

  // Build the font-family stack used by the reader. Honors BookProject.BodyFont
  // (the same value PDF/KDP/Ingram exports use, so screen and print stay in sync).
  // Falls back to a serif stack matching the export renderers' Georgia default
  // when no font is selected on the book.
  bodyFontStack = computed(() => {
    const chosen = this.book()?.bodyFont?.trim();
    const fallback = `'Crimson Pro', 'Source Serif Pro', Georgia, 'Times New Roman', serif`;
    return chosen ? `'${chosen.replace(/'/g, '')}', ${fallback}` : fallback;
  });

  // Chapter-level navigation availability used to decide whether a page step
  // should overflow into the previous/next chapter.
  private hasPrevChapter = computed(() => {
    const c = this.current();
    if (!c) return false;
    return c.chapterNumber > 1;
  });
  private hasNextChapter = computed(() => {
    const c = this.current();
    if (!c) return false;
    return this.chapters().some((x) => x.chapterNumber === c.chapterNumber + 1);
  });

  hasPrevPage = computed(() => this.pageIndex() > 0 || this.hasPrevChapter());
  hasNextPage = computed(() => this.pageIndex() < this.pageCount() - 1 || this.hasNextChapter());

  constructor() {
    // When the chapter changes (new rendered HTML), reset to page 0 and
    // recompute page count after the DOM commits. We defer via microtask so
    // the innerHTML binding has flushed.
    effect(() => {
      // touch both signals so the effect retracks
      this.renderedHtml();
      this.current();
      queueMicrotask(() => {
        this.pageIndex.set(0);
        this.recomputePageCount();
      });
    });
  }

  ngOnInit() {
    this.api.get(this.id).subscribe({
      next: (b) => {
        this.book.set(b);
        const first = b.chapters[0];
        if (first) this.selectChapter(first.id);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  ngAfterViewInit() {
    const vp = this.viewport()?.nativeElement;
    if (!vp) return;
    // Recompute page geometry on viewport resize. Pinned to the viewport so
    // sidebar collapses / window resizes propagate immediately.
    this.resizeObserver = new ResizeObserver(() => {
      this.recomputePageGeometry();
    });
    this.resizeObserver.observe(vp);
    this.recomputePageGeometry();
  }

  ngOnDestroy() {
    this.resizeObserver?.disconnect();
    if (this.flipTimer !== null) {
      window.clearTimeout(this.flipTimer);
    }
  }

  private recomputePageGeometry() {
    const vp = this.viewport()?.nativeElement;
    if (!vp) return;
    const rect = vp.getBoundingClientRect();
    const available = rect.width - this.horizontalPaddingPx * 2;
    const width = Math.max(this.minPageWidthPx, Math.min(this.maxPageWidthPx, Math.floor(available)));
    const height = Math.max(320, Math.floor(rect.height - this.verticalPaddingPx));
    const widthChanged = width !== this.pageWidth();
    const heightChanged = height !== this.pageHeight();
    if (widthChanged) this.pageWidth.set(width);
    if (heightChanged) this.pageHeight.set(height);
    if (widthChanged || heightChanged) {
      // Geometry change can change pagination; clamp current index after recount.
      queueMicrotask(() => this.recomputePageCount());
    }
  }

  private recomputePageCount() {
    const t = this.track()?.nativeElement;
    if (!t) {
      this.pageCount.set(1);
      return;
    }
    const step = this.pageWidth() + this.columnGap;
    // scrollWidth gives the total laid-out width of all columns; divide by
    // the per-page step to get the page count. Guard against zero width.
    const totalWidth = t.scrollWidth;
    const count = Math.max(1, Math.ceil((totalWidth + this.columnGap) / step));
    this.pageCount.set(count);
    // Clamp pageIndex if the chapter shrank.
    if (this.pageIndex() > count - 1) {
      this.pageIndex.set(count - 1);
    }
  }

  selectChapter(chapterId: string) {
    this.selectedId.set(chapterId);
    if (this.cachedChapters()[chapterId]) return;
    this.api.getChapter(this.id, chapterId).subscribe((ch) => {
      this.cachedChapters.update((map) => ({ ...map, [chapterId]: ch }));
    });
  }

  nextPage() {
    if (this.pageIndex() < this.pageCount() - 1) {
      this.triggerFlip('next');
      this.pageIndex.update((v) => v + 1);
      return;
    }
    // Last page of chapter - advance into the next chapter at page 0.
    const c = this.current();
    if (!c) return;
    const target = this.chapters().find((x) => x.chapterNumber === c.chapterNumber + 1);
    if (target) {
      this.triggerFlip('next');
      this.selectChapter(target.id);
    }
  }

  prevPage() {
    if (this.pageIndex() > 0) {
      this.triggerFlip('prev');
      this.pageIndex.update((v) => v - 1);
      return;
    }
    // First page - go back to the previous chapter's LAST page. We jump to
    // the last page after the new chapter renders by stashing a target.
    const c = this.current();
    if (!c) return;
    const target = this.chapters().find((x) => x.chapterNumber === c.chapterNumber - 1);
    if (!target) return;
    this.triggerFlip('prev');
    const wasCached = !!this.cachedChapters()[target.id];
    this.selectChapter(target.id);
    if (wasCached) {
      // Effect will reset pageIndex to 0 and recompute count; jump to last
      // page once that has happened.
      queueMicrotask(() => {
        // Recompute first then clamp to last page.
        this.recomputePageCount();
        this.pageIndex.set(this.pageCount() - 1);
      });
    } else {
      // For a fetch-on-demand chapter, the renderedHtml effect runs once the
      // chapter loads; intercept via a one-shot subscription on cachedChapters.
      const sub = this.api.getChapter(this.id, target.id).subscribe(() => {
        queueMicrotask(() => {
          this.recomputePageCount();
          this.pageIndex.set(this.pageCount() - 1);
        });
        sub.unsubscribe();
      });
    }
  }

  private triggerFlip(direction: 'next' | 'prev') {
    this.flipDirection.set(direction);
    if (this.flipTimer !== null) window.clearTimeout(this.flipTimer);
    this.flipTimer = window.setTimeout(() => {
      this.flipDirection.set(null);
      this.flipTimer = null;
    }, 620);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(ev: KeyboardEvent) {
    const target = ev.target as HTMLElement | null;
    // Ignore arrow keys when the user is typing in a form field.
    if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) {
      return;
    }
    if (ev.key === 'ArrowRight') {
      if (this.hasNextPage()) {
        ev.preventDefault();
        this.nextPage();
      }
    } else if (ev.key === 'ArrowLeft') {
      if (this.hasPrevPage()) {
        ev.preventDefault();
        this.prevPage();
      }
    }
  }

  regenerate() {
    const ch = this.current();
    if (!ch) return;
    this.regenerating.set(true);
    this.api.regenerateChapter(this.id, ch.id).subscribe({
      next: () => {
        this.regenerating.set(false);
        // Drop cached content so the next select will refetch when it's redrafted.
        this.cachedChapters.update((map) => {
          const copy = { ...map };
          delete copy[ch.id];
          return copy;
        });
      },
      error: () => this.regenerating.set(false),
    });
  }
}
