import {
  Component,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  BooksApi,
  BookCoverRow,
  CoverPaperType,
  CoverSide,
  CoverTemplate,
  UnsplashSearchResult,
} from '../../core/api/books.api';
import { environment } from '../../../environments/environment';

interface TemplateMeta {
  id: CoverTemplate;
  label: string;
  /** Short description shown under the template name in the gallery. */
  blurb: string;
  /** Tailwind classes for the card accent colour dot shown in the gallery. */
  accentClass: string;
}

@Component({
  selector: 'app-cover-picker',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <section class="glass rounded-xl p-5">
      <div class="flex items-center justify-between mb-5">
        <h2 class="font-display text-xl">Cover designer</h2>
        <span class="text-[10px] uppercase tracking-wider text-ink-500">
          Royalty-free via Unsplash
        </span>
      </div>

      @if (selectedCover(); as sel) {
        <!-- ============================================================
             TEMPLATE GALLERY
             Six CSS-only mockup cards in a 3-col grid. No server round-trip:
             everything you see here is rendered from Tailwind. The live preview
             (front + back) for the user's actual book happens in the preview pane
             below, on demand.
             ============================================================ -->
        <div class="mb-3 flex items-center justify-between">
          <div class="text-xs uppercase tracking-wider text-ink-400">Step 1 — Pick a template</div>
          <div class="text-[10px] text-ink-500">Both sides preview automatically on click</div>
        </div>
        <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 mb-6">
          @for (t of templates; track t.id) {
            <button type="button"
              (click)="selectTemplate(t.id, sel.id)"
              [class.ring-2]="activeTemplate() === t.id"
              [class.ring-brand-500]="activeTemplate() === t.id"
              [class.ring-offset-1]="activeTemplate() === t.id"
              [class.ring-offset-ink-950]="activeTemplate() === t.id"
              class="group text-left relative rounded-lg overflow-hidden border border-ink-700/60
                     hover:border-brand-500/60 hover:-translate-y-0.5 transition-all focus:outline-none
                     bg-ink-900/40">

              <!-- 2:3 cover thumbnail mockup — CSS-only, no network -->
              <div class="aspect-[2/3] w-full overflow-hidden">
                @switch (t.id) {
                  @case ('BoldGradient') {
                    <div class="w-full h-full relative bg-gradient-to-b from-slate-700 via-slate-900 to-black">
                      <div class="absolute inset-x-0 bottom-0 p-2.5">
                        <div class="w-8 h-0.5 bg-fuchsia-400 mb-1.5"></div>
                        <div class="text-white font-bold text-[11px] leading-tight lowercase">{{ mockTitle() }}</div>
                        <div class="text-[7px] tracking-[0.2em] text-white/70 uppercase mt-1.5">{{ mockAuthor() }}</div>
                      </div>
                    </div>
                  }
                  @case ('ClassicCentered') {
                    <div class="w-full h-full flex flex-col">
                      <div class="h-[55%] bg-gradient-to-br from-amber-700/60 to-amber-900/80"></div>
                      <div class="h-[45%] bg-[#F5E9CF] text-center px-2.5 flex flex-col justify-center">
                        <div class="text-[#5C5141] text-[8px]">✦</div>
                        <div class="text-stone-900 font-serif text-[10px] font-bold leading-tight mt-1">{{ mockTitle() }}</div>
                        <div class="h-px w-6 bg-[#5C5141] mx-auto my-1"></div>
                        <div class="text-[#5C5141] text-[7px] tracking-[0.15em] uppercase">{{ mockAuthor() }}</div>
                      </div>
                    </div>
                  }
                  @case ('ModernMinimal') {
                    <div class="w-full h-full bg-white p-2 flex flex-col">
                      <div class="text-gray-900 text-[9px] lowercase font-semibold leading-tight">{{ mockTitle() }}</div>
                      <div class="flex-1 my-1.5 bg-gradient-to-br from-slate-300 to-slate-500 rounded-sm"></div>
                      <div class="text-right text-gray-600 text-[7px] tracking-[0.15em] uppercase">{{ mockAuthor() }}</div>
                    </div>
                  }
                  @case ('PenguinStripe') {
                    <div class="w-full h-full flex flex-col">
                      <div class="h-[25%] bg-orange-600 flex items-center justify-center px-1.5">
                        <div class="text-orange-50 font-serif font-bold text-[9px] text-center leading-tight">{{ mockTitle() }}</div>
                      </div>
                      <div class="h-[50%] bg-gradient-to-br from-stone-300 to-stone-600"></div>
                      <div class="h-[25%] bg-orange-600 flex flex-col items-center justify-center px-1.5">
                        <div class="text-white text-[7px] tracking-[0.15em] uppercase">{{ mockAuthor() }}</div>
                      </div>
                    </div>
                  }
                  @case ('MagazineBlock') {
                    <div class="w-full h-full flex">
                      <div class="w-[55%] bg-gradient-to-br from-stone-400 to-stone-700"></div>
                      <div class="w-[45%] bg-rose-700 relative flex flex-col justify-end p-1">
                        <div class="absolute inset-0 flex items-center justify-center overflow-hidden">
                          <div class="text-white font-bold text-[10px] whitespace-nowrap"
                               style="transform: rotate(-90deg);">{{ mockTitle() }}</div>
                        </div>
                        <div class="relative text-white/70 text-[7px] tracking-[0.15em] uppercase">{{ mockAuthor() }}</div>
                      </div>
                    </div>
                  }
                  @case ('AuthorSpotlight') {
                    <div class="w-full h-full bg-gradient-to-b from-slate-900 via-indigo-950 to-black relative">
                      <div class="absolute inset-x-0 top-2 px-2 text-center">
                        <div class="text-indigo-300 text-[7px] tracking-[0.2em] uppercase">{{ mockAuthor() }}</div>
                      </div>
                      <div class="absolute inset-0 flex items-center justify-center px-2">
                        <div class="text-center w-full">
                          <div class="text-white font-serif font-bold text-[11px] leading-tight">{{ mockTitle() }}</div>
                          <div class="text-indigo-200 font-serif italic text-[8px] mt-1">{{ mockSubtitle() }}</div>
                        </div>
                      </div>
                    </div>
                  }
                  @case ('NightOwl') {
                    <div class="w-full h-full relative bg-gradient-to-b from-slate-800 via-slate-900 to-black">
                      <!-- Simulated blurred photo layer -->
                      <div class="absolute inset-0 bg-gradient-to-br from-blue-950/40 to-black/80"></div>
                      <div class="absolute inset-x-0 bottom-0 p-2.5">
                        <div class="w-7 h-[2.5px] bg-blue-400 mb-2"></div>
                        <div class="text-white font-bold text-[11px] leading-tight">{{ mockTitle() }}</div>
                        <div class="text-[7px] tracking-[0.2em] text-blue-300/80 uppercase mt-1.5">{{ mockAuthor() }}</div>
                      </div>
                    </div>
                  }
                  @case ('GoldenAge') {
                    <div class="w-full h-full relative bg-gradient-to-br from-amber-800/70 to-stone-900">
                      <!-- Warm vignette overlay -->
                      <div class="absolute inset-0 bg-gradient-to-t from-black/70 via-transparent to-amber-900/20"></div>
                      <!-- Decorative border -->
                      <div class="absolute inset-[10px] border border-yellow-600/40 pointer-events-none"></div>
                      <div class="absolute inset-x-0 bottom-0 p-2.5 text-center">
                        <div class="text-yellow-400/90 text-[8px] mb-1.5">✦ ✦ ✦</div>
                        <div class="text-white font-serif font-bold text-[11px] leading-tight">{{ mockTitle() }}</div>
                        <div class="text-yellow-200/80 font-serif italic text-[8px] mt-1">{{ mockSubtitle() }}</div>
                        <div class="text-yellow-400/80 text-[7px] tracking-[0.2em] uppercase mt-1.5">{{ mockAuthor() }}</div>
                      </div>
                    </div>
                  }
                }
              </div>

              <!-- Label + blurb -->
              <div class="px-2.5 py-2 border-t border-ink-700/60 bg-ink-900/60 flex items-start gap-1.5">
                <span class="mt-1 shrink-0 w-2 h-2 rounded-full {{ t.accentClass }}"></span>
                <div>
                  <div class="font-display text-[12px] text-ink-100 leading-tight">{{ t.label }}</div>
                  <div class="text-[10px] text-ink-400 mt-0.5 leading-snug">{{ t.blurb }}</div>
                </div>
              </div>

              @if (activeTemplate() === t.id) {
                <div class="absolute top-1.5 right-1.5 bg-brand-600 text-white text-[9px] uppercase
                            tracking-wider px-1.5 py-0.5 rounded-full shadow">
                  ✓
                </div>
              }
            </button>
          }
        </div>

        <!-- ============================================================
             LIVE PREVIEW PANE (Front + Back)
             Two big 6:9 cards. Renders the user's actual book composed by
             the API. Each card has Apply / Preview actions.
             ============================================================ -->
        <div class="mb-3 flex items-center justify-between">
          <div class="text-xs uppercase tracking-wider text-ink-400">Step 2 — Preview &amp; apply</div>
          <div class="text-[10px] text-ink-500">Front renders when you pick a template · Apply saves it to exports</div>
        </div>
        <div class="grid grid-cols-1 md:grid-cols-3 gap-5 mb-6">
          <!-- SOURCE PHOTO — the raw Unsplash / AI image being designed on top of.
               Same dimensions as Front/Back so the three cards line up visually. -->
          <div class="flex flex-col">
            <div class="text-[11px] uppercase tracking-wider text-ink-300 mb-2">Source photo</div>
            <div class="cover-3d aspect-[2/3] max-w-[360px] w-full mx-auto md:mx-0
                        rounded-md overflow-hidden bg-ink-900 relative shadow-2xl shadow-black/40">
              @if (sel.assetUrl) {
                <img [src]="absoluteUrl(sel.assetUrl)" alt="Source photo"
                     class="w-full h-full object-cover" />
              } @else {
                <div class="w-full h-full border-2 border-dashed border-ink-700 rounded-md
                            flex flex-col items-center justify-center text-center px-4">
                  <div class="text-3xl text-ink-600 mb-2">&#9633;</div>
                  <div class="text-xs text-ink-400">No source photo yet</div>
                </div>
              }
            </div>
            <div class="mt-3 max-w-[360px] w-full mx-auto md:mx-0 text-xs">
              <div class="text-emerald-300 uppercase tracking-wider text-[10px]">Active source</div>
              <div class="text-ink-200 mt-1 truncate">{{ sel.prompt }}</div>
              <div class="text-ink-500 mt-0.5">{{ sel.style }}</div>
              @if (attributionLine(sel); as attr) {
                <div class="text-ink-400 mt-2 leading-snug">
                  {{ attr.text }}
                  @if (attr.url) {
                    <a [href]="attr.url" target="_blank" rel="noopener"
                       class="text-brand-300 hover:text-brand-200 ml-1">&#8599;</a>
                  }
                </div>
              }
            </div>
          </div>

          <!-- FRONT -->
          <div class="flex flex-col">
            <div class="text-[11px] uppercase tracking-wider text-ink-300 mb-2">Front cover</div>
            <div class="cover-3d aspect-[2/3] max-w-[360px] w-full mx-auto md:mx-0
                        rounded-md overflow-hidden bg-ink-900 relative shadow-2xl shadow-black/40">
              @if (composingFront()) {
                <div class="absolute inset-0 grid place-items-center bg-ink-900/80">
                  <div class="flex flex-col items-center gap-2">
                    <div class="w-8 h-8 rounded-full border-2 border-brand-500/30
                                border-t-brand-400 animate-spin"></div>
                    <div class="text-[10px] text-ink-300 uppercase tracking-wider">Composing&hellip;</div>
                  </div>
                </div>
              } @else if (previewFrontUrl()) {
                <img [src]="previewFrontUrl()!" alt="Front preview" class="w-full h-full object-cover" />
              } @else if (sel.designedAssetUrl) {
                <img [src]="absoluteUrl(sel.designedAssetUrl) + '?v=' + cacheBust()"
                     alt="Designed front" class="w-full h-full object-cover" />
              } @else {
                <div class="w-full h-full border-2 border-dashed border-ink-700 rounded-md
                            flex flex-col items-center justify-center text-center px-4">
                  <div class="text-3xl text-ink-600 mb-2">&#9633;</div>
                  <div class="text-xs text-ink-400">Pick a template, then click Apply</div>
                </div>
              }
            </div>
            <div class="mt-3 flex flex-wrap gap-2 max-w-[360px] w-full mx-auto md:mx-0">
              <button type="button"
                (click)="applyDesign(sel.id, 'Front')"
                [disabled]="!canApplyFront() || composingFront() || composingBack()"
                [title]="canApplyFront() ? '' : 'This design is already applied'"
                class="flex-1 min-w-0 bg-gradient-to-r from-brand-600 to-fuchsia-600
                       hover:from-brand-500 hover:to-fuchsia-500
                       disabled:opacity-40 disabled:cursor-not-allowed
                       text-white text-sm rounded px-3 py-2 transition">
                {{ composingFront() ? 'Applying&hellip;' : 'Apply this design' }}
              </button>
              <button type="button"
                (click)="previewSide(sel.id, 'Front')"
                [disabled]="composingFront() || composingBack()"
                class="bg-ink-800 hover:bg-ink-700 border border-ink-700 disabled:opacity-40
                       text-ink-100 text-sm rounded px-3 py-2 transition">
                Preview
              </button>
            </div>
          </div>

          <!-- BACK -->
          <div class="flex flex-col">
            <div class="text-[11px] uppercase tracking-wider text-ink-300 mb-2">Back cover</div>
            <div class="cover-3d aspect-[2/3] max-w-[360px] w-full mx-auto md:mx-0
                        rounded-md overflow-hidden bg-ink-900 relative shadow-2xl shadow-black/40">
              @if (composingBack()) {
                <div class="absolute inset-0 grid place-items-center bg-ink-900/80">
                  <div class="flex flex-col items-center gap-2">
                    <div class="w-8 h-8 rounded-full border-2 border-brand-500/30
                                border-t-brand-400 animate-spin"></div>
                    <div class="text-[10px] text-ink-300 uppercase tracking-wider">Composing&hellip;</div>
                  </div>
                </div>
              } @else if (previewBackUrl()) {
                <img [src]="previewBackUrl()!" alt="Back preview" class="w-full h-full object-cover" />
              } @else {
                <div class="w-full h-full border-2 border-dashed border-ink-700 rounded-md
                            flex flex-col items-center justify-center text-center px-4">
                  <div class="text-3xl text-ink-600 mb-2">&#9633;</div>
                  <div class="text-xs text-ink-400">Click Preview to render the back cover</div>
                </div>
              }
            </div>
            <div class="mt-3 flex flex-wrap gap-2 max-w-[360px] w-full mx-auto md:mx-0">
              <button type="button"
                (click)="applyDesign(sel.id, 'Back')"
                [disabled]="composingFront() || composingBack()"
                class="flex-1 min-w-0 bg-gradient-to-r from-brand-600 to-fuchsia-600
                       hover:from-brand-500 hover:to-fuchsia-500
                       disabled:opacity-40 text-white text-sm rounded px-3 py-2 transition">
                {{ composingBack() ? 'Composing&hellip;' : 'Compose back' }}
              </button>
              <button type="button"
                (click)="previewSide(sel.id, 'Back')"
                [disabled]="composingFront() || composingBack()"
                class="bg-ink-800 hover:bg-ink-700 border border-ink-700 disabled:opacity-40
                       text-ink-100 text-sm rounded px-3 py-2 transition">
                Preview
              </button>
            </div>
          </div>
        </div>

        @if (designError()) {
          <div class="mb-6 text-xs text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
            {{ designError() }}
          </div>
        }

        <!-- ============================================================
             PRINT WRAP - collapsible
             ============================================================ -->
        <div class="glass rounded-xl p-5 mb-6">
          <div class="flex items-center justify-between">
            <div>
              <div class="font-display text-base text-ink-100">Print wrap (KDP / Ingram)</div>
              <div class="text-xs text-ink-400 mt-0.5">
                Combines back + spine + front in one print-ready PDF. Upload directly to KDP or IngramSpark.
              </div>
            </div>
            <button type="button"
              (click)="toggleWrapPanel()"
              class="bg-ink-800 hover:bg-ink-700 border border-ink-700 text-ink-100 text-sm rounded px-3 py-2 shrink-0">
              {{ showWrapPanel() ? 'Hide' : 'Configure wrap PDF' }}
            </button>
          </div>

          @if (showWrapPanel()) {
            <div class="mt-4 flex flex-wrap items-end gap-3 pt-4 border-t border-ink-700/40">
              <label class="flex flex-col text-xs text-ink-300">
                <span class="mb-1 uppercase tracking-wider">Page count</span>
                <input type="number" [formControl]="pageCount" min="24" max="800"
                  class="w-28 bg-ink-900/60 border border-ink-700 rounded px-2 py-1 text-sm text-ink-100 outline-none focus:border-brand-500" />
              </label>
              <label class="flex flex-col text-xs text-ink-300">
                <span class="mb-1 uppercase tracking-wider">Paper</span>
                <select [formControl]="paperType"
                  class="bg-ink-900/60 border border-ink-700 rounded px-2 py-1 text-sm text-ink-100 outline-none focus:border-brand-500">
                  <option value="cream">Cream (KDP)</option>
                  <option value="white">White (KDP)</option>
                </select>
              </label>
              <button type="button"
                (click)="downloadWrap(sel.id)"
                [disabled]="wrapDownloading()"
                class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500
                       disabled:opacity-50 text-white text-sm rounded px-4 py-2 transition">
                {{ wrapDownloading() ? 'Rendering&hellip;' : 'Download wrap PDF' }}
              </button>
              <div class="text-[10px] text-ink-500 max-w-xs">
                Spine width = pages &times; 0.0025" (cream) or &times; 0.002" (white). Adjust if your
                actual page count differs from the chapter-word-count estimate.
              </div>
            </div>
          }
        </div>
      }

      <!-- ============================================================
           SOURCE PHOTO SWAPPING
           Pushed below the design tools because picking a new photo is a
           less-frequent "restart" action.
           ============================================================ -->
      <div class="border-t border-ink-700/40 pt-5">
        <div class="mb-3">
          <div class="font-display text-base text-ink-100">Change the source photo</div>
          <div class="text-xs text-ink-400 mt-0.5">
            Pick a new background image. Your typography template stays the same.
          </div>
        </div>

        <!-- Search -->
        <form (submit)="$event.preventDefault(); search()" class="flex gap-2 mb-3">
          <input
            type="text"
            [formControl]="query"
            [placeholder]="defaultQuery() || 'e.g. quiet mountain morning, dusk shoreline, vintage typewriter'"
            class="flex-1 bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm" />
          <button type="submit" [disabled]="searching() || (!query.value && !defaultQuery())"
            class="bg-brand-600 hover:bg-brand-500 disabled:opacity-50 text-white text-sm rounded px-4 py-2">
            {{ searching() ? 'Searching&hellip;' : 'Search Unsplash' }}
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
              {{ generatingAi() ? 'Generating&hellip;' : 'Generate with AI' }}
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
                    Saving&hellip;
                  </div>
                }
              </button>
            }
          </div>
        } @else if (!searching() && hasSearched()) {
          <div class="text-sm text-ink-400">No results. Try a different query.</div>
        }
      </div>
    </section>
  `,
  styles: [`
    /* Faux 3D book look. The 4px inset shadow on the left simulates the spine, so each
       preview reads as a bound book rather than a flat image. Subtle enough that it
       doesn't fight the cover artwork. */
    .cover-3d {
      box-shadow:
        inset 4px 0 6px -2px rgba(0, 0, 0, 0.65),
        inset -1px 0 0 rgba(255, 255, 255, 0.04),
        0 25px 50px -12px rgba(0, 0, 0, 0.5);
    }
  `],
})
export class CoverPickerComponent implements OnInit, OnChanges, OnDestroy {
  @Input() projectId!: string;
  @Input() bookTitle?: string | null;
  @Input() bookSubtitle?: string | null;
  @Input() bookGenre?: string | null;
  @Input() bookTone?: string | null;
  @Input() bookAuthor?: string | null;

  private api = inject(BooksApi);

  query = new FormControl<string>('', { nonNullable: true, validators: [Validators.maxLength(120)] });
  aiPrompt = new FormControl<string>('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  aiStyle = new FormControl<string>('', { nonNullable: true });
  pageCount = new FormControl<number>(300, { nonNullable: true });
  paperType = new FormControl<CoverPaperType>('cream', { nonNullable: true });
  results = signal<UnsplashSearchResult[]>([]);
  covers = signal<BookCoverRow[]>([]);
  searching = signal(false);
  selecting = signal<string | null>(null);
  generatingAi = signal(false);
  hasSearched = signal(false);
  errorMessage = signal<string | null>(null);
  notConfigured = signal(false);
  aiNotConfigured = signal(false);

  // Design system state. Two parallel "composing" flags (front + back) so we can render
  // both spinners independently - users frequently click Preview-front then Preview-back
  // in quick succession and we don't want to gate them on a single global flag.
  activeTemplate = signal<CoverTemplate>('BoldGradient');
  composingFront = signal(false);
  composingBack = signal(false);
  previewFrontUrl = signal<string | null>(null);
  previewBackUrl = signal<string | null>(null);
  designError = signal<string | null>(null);
  showWrapPanel = signal(false);
  wrapDownloading = signal(false);
  /** Bumped on every successful Apply so the <img src> re-fetches the persisted asset
   * after a redesign. The persisted designed-image endpoint sets immutable-cache headers
   * (1y), so we MUST cache-bust manually or the browser shows the stale render. */
  cacheBust = signal<number>(Date.now());

  /** Eight templates with one-line blurbs. The visual mockup for each is rendered inline
   * in the template gallery (CSS-only, no server round-trip). Clicking a template both
   * selects it AND auto-previews both sides so Step 2 always shows a fresh render. */
  templates: TemplateMeta[] = [
    { id: 'BoldGradient',    label: 'Bold gradient',    blurb: 'Modern, dramatic, thriller', accentClass: 'bg-fuchsia-500' },
    { id: 'ClassicCentered', label: 'Classic centered', blurb: 'Full cream back, Penguin style', accentClass: 'bg-amber-600' },
    { id: 'ModernMinimal',   label: 'Modern minimal',   blurb: 'Clean white margins, quiet', accentClass: 'bg-slate-400' },
    { id: 'PenguinStripe',   label: 'Penguin stripe',   blurb: 'Bold colour bands, literary', accentClass: 'bg-orange-500' },
    { id: 'MagazineBlock',   label: 'Magazine block',   blurb: 'Side-by-side split, graphic', accentClass: 'bg-rose-600' },
    { id: 'AuthorSpotlight', label: 'Author spotlight', blurb: 'Cinematic, navy header band', accentClass: 'bg-indigo-500' },
    { id: 'NightOwl',        label: 'Night owl',        blurb: 'Dark noir, electric-blue accent', accentClass: 'bg-blue-500' },
    { id: 'GoldenAge',       label: 'Golden age',       blurb: 'Warm vintage, aged-paper back', accentClass: 'bg-yellow-600' },
  ];

  selectedCover = computed(() => this.covers().find((c) => c.status === 'Selected') ?? null);

  /** Mockup strings - shown in the CSS-rendered gallery cards above. Fall back to
   * generic copy if the book hasn't been set up yet, so the gallery still looks like a
   * gallery of real books rather than empty placeholders. */
  mockTitle = computed(() => this.bookTitle?.trim() || 'The Apprentice\'s Compass');
  mockSubtitle = computed(() => this.bookSubtitle?.trim() || 'From Boilermaker to Manager');
  mockAuthor = computed(() => (this.bookAuthor?.trim() || 'Anton de Jager').toUpperCase());

  /**
   * Server returns relative asset URLs like `/api/books/{id}/covers/{cid}/designed-image`.
   * `<img src>` doesn't go through the auth interceptor, so a relative URL resolves
   * against the SPA host (madauthor.madprospects.com) which has no /api - 404.
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

  /** Whether the Apply-front button should be enabled. False when the currently selected
   * template matches what's already persisted on the cover (we infer this from the
   * `style` column, which is set to `... · {Template}` after every Apply). */
  canApplyFront = computed(() => {
    const sel = this.selectedCover();
    if (!sel) return false;
    if (!sel.designedAssetUrl) return true; // nothing applied yet → always allow
    const persistedTemplate = this.parsePersistedTemplate(sel.style);
    return persistedTemplate !== this.activeTemplate();
  });

  /** Extract the trailing `· {Template}` token the server appends to `BookCover.Style`
   * after a Design call. Returns the template id or null. */
  private parsePersistedTemplate(style: string | null | undefined): CoverTemplate | null {
    if (!style) return null;
    const parts = style.split('·').map((s) => s.trim());
    const tail = parts[parts.length - 1];
    const valid: CoverTemplate[] = [
      'BoldGradient', 'ClassicCentered', 'ModernMinimal',
      'PenguinStripe', 'MagazineBlock', 'AuthorSpotlight',
    ];
    return (valid as string[]).includes(tail) ? (tail as CoverTemplate) : null;
  }

  ngOnInit() {
    this.fetchCovers();
  }

  ngOnChanges() {
    if (this.projectId && this.covers().length === 0) this.fetchCovers();
  }

  fetchCovers() {
    if (!this.projectId) return;
    this.api.listCovers(this.projectId).subscribe((list) => {
      this.covers.set(list);
      // Sync the active-template chip to whatever's persisted, so the "already applied"
      // check works on first load.
      const sel = list.find((c) => c.status === 'Selected');
      if (sel) {
        const persisted = this.parsePersistedTemplate(sel.style);
        if (persisted) this.activeTemplate.set(persisted);
      }
    });
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

  // ---- Design / preview / wrap controls ----------------------------------

  /**
   * Selecting a template immediately triggers both-sides preview so the user never
   * has to manually click "Preview" — the renders appear while they're still reading
   * the template label. Calls are independent (no await), so front and back render
   * in parallel. We guard against rapid switching: if a render is already in-flight
   * for a side the composing flag is true, and previewSide() returns early.
   */
  selectTemplate(t: CoverTemplate, coverId?: string) {
    if (this.activeTemplate() === t) return; // already selected — nothing to do
    this.activeTemplate.set(t);
    this.clearPreview('Front');
    this.clearPreview('Back');
    this.designError.set(null);

    // Auto-preview both sides immediately after switching templates.
    if (coverId) {
      this.previewSide(coverId, 'Front');
      this.previewSide(coverId, 'Back');
    }
  }

  /**
   * Live preview for either side. Calls the design-preview endpoint via HttpClient
   * (which carries the auth header), receives a PNG blob, exposes it as an object URL
   * for <img src>. The previous URL is revoked first to avoid a memory leak on rapid
   * template-swap clicks.
   */
  previewSide(coverId: string, side: CoverSide) {
    if (side === 'Front' ? this.composingFront() : this.composingBack()) return;
    if (side === 'Front') this.composingFront.set(true); else this.composingBack.set(true);
    this.designError.set(null);
    this.api.designPreview(this.projectId, coverId, this.activeTemplate(), side).subscribe({
      next: (blob) => {
        if (side === 'Front') this.composingFront.set(false); else this.composingBack.set(false);
        this.setPreviewBlob(side, blob);
      },
      error: (err) => {
        if (side === 'Front') this.composingFront.set(false); else this.composingBack.set(false);
        // The blob-mode HttpClient call swallows the JSON body; parse it back.
        this.designError.set(this.parseBlobError(err, `Could not preview ${side === 'Front' ? 'front' : 'back'} of cover.`));
      },
    });
  }

  /**
   * Composes and persists the design. For Front, the new image becomes the cover's
   * `designedAssetUrl` and the cover is promoted to Selected. For Back, the result is
   * shown inline as a preview only (back covers aren't part of the storefront image
   * gallery - they live exclusively in the wrap PDF).
   */
  applyDesign(coverId: string, side: CoverSide) {
    if (side === 'Front' ? this.composingFront() : this.composingBack()) return;
    if (side === 'Front') this.composingFront.set(true); else this.composingBack.set(true);
    this.designError.set(null);
    this.api.designCover(this.projectId, coverId, this.activeTemplate(), side).subscribe({
      next: () => {
        if (side === 'Front') this.composingFront.set(false); else this.composingBack.set(false);
        if (side === 'Front') {
          // Clear the local preview blob so the persisted URL takes over the front card.
          this.clearPreview('Front');
          this.cacheBust.set(Date.now());
          this.fetchCovers();
        } else {
          // Back gets a fresh preview rendered so the user sees the result inline.
          this.previewSide(coverId, 'Back');
        }
      },
      error: (err) => {
        if (side === 'Front') this.composingFront.set(false); else this.composingBack.set(false);
        this.designError.set(err?.error?.error
          ?? `Could not ${side === 'Front' ? 'design' : 'compose back of'} cover.`);
      },
    });
  }

  private setPreviewBlob(side: CoverSide, blob: Blob) {
    const url = URL.createObjectURL(blob);
    if (side === 'Front') {
      const old = this.previewFrontUrl();
      if (old) URL.revokeObjectURL(old);
      this.previewFrontUrl.set(url);
    } else {
      const old = this.previewBackUrl();
      if (old) URL.revokeObjectURL(old);
      this.previewBackUrl.set(url);
    }
  }

  private clearPreview(side: CoverSide) {
    if (side === 'Front') {
      const old = this.previewFrontUrl();
      if (old) { URL.revokeObjectURL(old); this.previewFrontUrl.set(null); }
    } else {
      const old = this.previewBackUrl();
      if (old) { URL.revokeObjectURL(old); this.previewBackUrl.set(null); }
    }
  }

  /** When HttpClient receives an error with responseType:'blob' the body is a Blob, not
   * the parsed JSON. Try to read it as text and extract the `error` field. Falls back to
   * the supplied default message. */
  private parseBlobError(err: any, fallback: string): string {
    if (err?.error instanceof Blob) {
      // Fire-and-forget read; surface fallback synchronously, and update the message
      // once the blob is parsed.
      err.error.text().then((text: string) => {
        try {
          const parsed = JSON.parse(text);
          if (parsed?.error) this.designError.set(parsed.error);
        } catch { /* keep fallback */ }
      });
    } else if (err?.error?.error) {
      return err.error.error;
    }
    return fallback;
  }

  toggleWrapPanel() {
    this.showWrapPanel.update((v) => !v);
  }

  /**
   * Calls the wrap endpoint, reads the binary PDF, and triggers a download via a
   * temporary anchor. Filename comes from Content-Disposition, with a safe fallback
   * so we never produce a `null.pdf` if the header is stripped by a proxy.
   */
  downloadWrap(coverId: string) {
    if (this.wrapDownloading()) return;
    this.wrapDownloading.set(true);
    this.designError.set(null);
    const pages = this.pageCount.value > 0 ? this.pageCount.value : undefined;
    this.api
      .wrapCover(this.projectId, coverId, this.activeTemplate(), this.paperType.value, pages)
      .subscribe({
        next: (resp) => {
          this.wrapDownloading.set(false);
          if (!resp.body) return;
          const disp = resp.headers.get('Content-Disposition') ?? '';
          const match = /filename="?([^";]+)"?/i.exec(disp);
          const fileName = match?.[1] ?? `${(this.bookTitle ?? 'book').replace(/[^a-z0-9]+/gi, '-').toLowerCase()}-cover-wrap.pdf`;
          const url = URL.createObjectURL(resp.body);
          const a = document.createElement('a');
          a.href = url;
          a.download = fileName;
          document.body.appendChild(a);
          a.click();
          a.remove();
          URL.revokeObjectURL(url);
        },
        error: (err) => {
          this.wrapDownloading.set(false);
          this.designError.set(this.parseBlobError(err, 'Could not render wrap PDF.'));
        },
      });
  }

  ngOnDestroy(): void {
    const f = this.previewFrontUrl();
    if (f) URL.revokeObjectURL(f);
    const b = this.previewBackUrl();
    if (b) URL.revokeObjectURL(b);
  }
}
