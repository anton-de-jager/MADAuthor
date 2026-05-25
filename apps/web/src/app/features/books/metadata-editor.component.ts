import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { BooksApi } from '../../core/api/books.api';

/**
 * Editable view of the Publisher agent's output. The Publisher leaves
 * personal placeholders like [Pen Name], [SPOUSE NAME], [BETA READER 1]
 * inside the JSON it produces; this component lets the author fill them in.
 *
 * The component owns a single in-memory copy of the full JSON document so
 * unknown fields (suggestedCategories, bisacCodes, endorsementsScaffold
 * variants the prompt may add later) are round-tripped untouched.
 */
@Component({
  selector: 'app-metadata-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    @if (loading()) {
      <div class="text-sm text-ink-400">Loading publisher metadata…</div>
    } @else if (notReady()) {
      <div class="text-sm text-ink-400">
        Publisher metadata isn't ready yet. Once the AI Publisher agent has
        produced the marketing copy for your book, the form to fill in your
        personal details (pen name, dedication, acknowledgements…) will appear here.
      </div>
    } @else if (loadError()) {
      <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
        {{ loadError() }}
      </div>
    } @else if (formData) {
      <!-- Blanks helper -->
      <div class="mb-5 flex items-start gap-3 flex-wrap">
        <button type="button" (click)="scanBlanks()"
          class="text-xs bg-ink-800 hover:bg-ink-700 border border-ink-700 rounded px-3 py-1.5 text-ink-200">
          Find blanks
        </button>
        @if (blanksScanned() && blanks().length === 0) {
          <span class="text-xs text-emerald-300">No [BLANK] placeholders left - looking good!</span>
        }
        @if (blanks().length > 0) {
          <span class="text-xs text-ink-400 self-center">
            {{ blanks().length }} placeholder(s) still need your input:
          </span>
          <div class="flex flex-wrap gap-1.5">
            @for (b of blanks(); track b) {
              <span class="inline-block text-[11px] px-2 py-0.5 rounded-full bg-rose-900/40 border border-rose-700/50 text-rose-200">
                {{ b }}
              </span>
            }
          </div>
        }
      </div>

      <!-- About this book -->
      <fieldset class="mb-6">
        <legend class="text-xs uppercase tracking-wider text-brand-300 mb-2">About this book</legend>

        <label class="block text-sm text-ink-300 mt-3 mb-1">
          KDP description
          <span class="text-ink-500 ml-1"
            [class.text-rose-300]="(formData.kdpDescription?.length || 0) > 4000">
            ({{ formData.kdpDescription?.length || 0 }} / 4000)
          </span>
        </label>
        <textarea
          [(ngModel)]="formData.kdpDescription"
          rows="8"
          maxlength="4000"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm font-mono"
          placeholder="HTML-formatted long description for the Amazon KDP listing."></textarea>

        <label class="block text-sm text-ink-300 mt-4 mb-1">Short description (1–2 sentence hook)</label>
        <textarea
          [(ngModel)]="formData.shortDescription"
          rows="2"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm"></textarea>

        <label class="block text-sm text-ink-300 mt-4 mb-1">Refined subtitle (overrides the original if filled)</label>
        <input
          [(ngModel)]="formData.refinedSubtitle"
          type="text"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm" />

        <label class="block text-sm text-ink-300 mt-4 mb-1">
          Keywords (comma-separated, max 7)
          <span class="text-ink-500 ml-1"
            [class.text-rose-300]="keywordCount() > 7">
            ({{ keywordCount() }} / 7)
          </span>
        </label>
        <textarea
          [(ngModel)]="keywordsText"
          (ngModelChange)="onKeywordsChange($event)"
          rows="2"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm"
          placeholder="e.g. memoir, leadership, boilermaker, south africa, mentorship, second chances, manufacturing"></textarea>
      </fieldset>

      <!-- Front matter -->
      <fieldset class="mb-6">
        <legend class="text-xs uppercase tracking-wider text-brand-300 mb-2">Front matter</legend>

        <label class="block text-sm text-ink-300 mt-3 mb-1">Dedication (Markdown)</label>
        <textarea
          [(ngModel)]="formData.dedication"
          rows="4"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm font-mono"></textarea>

        <label class="block text-sm text-ink-300 mt-4 mb-1">Acknowledgements (Markdown)</label>
        <textarea
          [(ngModel)]="formData.acknowledgements"
          rows="6"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm font-mono"></textarea>

        <label class="block text-sm text-ink-300 mt-4 mb-1">ISBN page text (copyright page)</label>
        <textarea
          [(ngModel)]="formData.isbnPageText"
          rows="5"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm font-mono"></textarea>

        <label class="block text-sm text-ink-300 mt-4 mb-1">Copyright (single line)</label>
        <input
          [(ngModel)]="formData.copyrightText"
          type="text"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm" />
      </fieldset>

      <!-- Author -->
      <fieldset class="mb-6">
        <legend class="text-xs uppercase tracking-wider text-brand-300 mb-2">Author</legend>
        <label class="block text-sm text-ink-300 mt-3 mb-1">
          Author bio
          <span class="text-ink-500 ml-1"
            [class.text-amber-300]="authorBioWords() < 80 || authorBioWords() > 120">
            ({{ authorBioWords() }} words · target 80–120)
          </span>
        </label>
        <textarea
          [(ngModel)]="formData.authorBio"
          (ngModelChange)="onAuthorBioChange($event)"
          rows="6"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm"></textarea>
      </fieldset>

      <!-- Endorsements -->
      <fieldset class="mb-6">
        <legend class="text-xs uppercase tracking-wider text-brand-300 mb-2">Endorsements</legend>
        <label class="block text-sm text-ink-300 mt-3 mb-1">
          Endorsements scaffold (replace placeholders with real quotes as you collect them)
        </label>
        <textarea
          [(ngModel)]="formData.endorsementsScaffold"
          rows="8"
          class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm font-mono"></textarea>
      </fieldset>

      <!-- Save bar -->
      <div class="flex items-center gap-3">
        <button type="button" (click)="save()" [disabled]="saving()"
          class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 disabled:opacity-50 text-white font-medium rounded-lg px-5 py-2 text-sm">
          {{ saving() ? 'Saving…' : 'Save changes' }}
        </button>
        @if (saveMessage(); as msg) {
          <span class="text-sm"
            [class.text-emerald-300]="msg.kind === 'ok'"
            [class.text-rose-300]="msg.kind === 'err'">
            {{ msg.text }}
          </span>
        }
      </div>
    }
  `,
})
export class MetadataEditorComponent implements OnInit {
  @Input() projectId!: string;

  private api = inject(BooksApi);

  loading = signal(true);
  notReady = signal(false);
  loadError = signal<string | null>(null);
  saving = signal(false);
  saveMessage = signal<{ kind: 'ok' | 'err'; text: string } | null>(null);

  // The full JSON document. Kept as a plain property (not a signal) so that
  // [(ngModel)] two-way bindings can write back into it directly. Unknown
  // fields round-trip untouched on save.
  formData: any = null;

  // Keywords are stored as an array on disk, but easiest to edit as comma-text.
  keywordsText = '';

  // Bio word count needs to react when the user types; ngModel writes to the
  // plain object so we mirror the count into a signal via onAuthorBioChange.
  private authorBioSignal = signal('');

  blanks = signal<string[]>([]);
  blanksScanned = signal(false);

  keywordCount = computed(() => {
    const t = (this.keywordsTextSignal() || '').trim();
    if (!t) return 0;
    return t.split(',').map((s) => s.trim()).filter(Boolean).length;
  });

  // We can't reactively track a plain property, so the keyword count is wired
  // through this manual mirror that the textarea binding updates.
  private keywordsTextSignal = signal('');

  authorBioWords = computed(() => {
    const bio = (this.authorBioSignal() || '').trim();
    if (!bio) return 0;
    return bio.split(/\s+/).filter(Boolean).length;
  });

  ngOnInit() {
    this.fetch();
  }

  private fetch() {
    if (!this.projectId) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.api.getPublisherMetadata(this.projectId).subscribe({
      next: (doc) => {
        this.formData = doc ?? {};
        const kws = Array.isArray(doc?.keywords) ? doc.keywords : [];
        this.keywordsText = kws.join(', ');
        this.keywordsTextSignal.set(this.keywordsText);
        this.authorBioSignal.set(this.formData.authorBio || '');
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        if (err.status === 404) {
          this.notReady.set(true);
        } else {
          this.loadError.set(err?.error?.error ?? 'Could not load publisher metadata.');
        }
      },
    });
  }

  onAuthorBioChange(value: string) {
    this.authorBioSignal.set(value || '');
  }

  onKeywordsChange(value: string) {
    this.keywordsTextSignal.set(value || '');
  }

  scanBlanks() {
    this.blanksScanned.set(true);
    const d = this.formData;
    if (!d) {
      this.blanks.set([]);
      return;
    }
    const pattern = /\[[A-Z][^\]]*\]/g;
    const found = new Set<string>();
    const fields = [
      'kdpDescription', 'shortDescription', 'refinedSubtitle',
      'dedication', 'acknowledgements', 'isbnPageText', 'copyrightText',
      'authorBio', 'endorsementsScaffold',
    ];
    for (const f of fields) {
      const v = d[f];
      if (typeof v !== 'string' || !v) continue;
      const matches = v.match(pattern);
      if (matches) matches.forEach((m) => found.add(m));
    }
    // Also scan the keywords text just in case the author left a placeholder there.
    const km = (this.keywordsText || '').match(pattern);
    if (km) km.forEach((m) => found.add(m));
    this.blanks.set(Array.from(found).sort());
  }

  save() {
    if (!this.formData) return;
    // Sync the keywords text back into the array shape the server expects.
    const kws = (this.keywordsText || '')
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    this.formData.keywords = kws;
    // Mirror the live keywords text into the count signal in case the user
    // typed but never blurred (ngModel writes immediately, so this is a safety net).
    this.keywordsTextSignal.set(this.keywordsText || '');
    this.saving.set(true);
    this.saveMessage.set(null);
    this.api.putPublisherMetadata(this.projectId, this.formData).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.formData = saved ?? this.formData;
        const k = Array.isArray(saved?.keywords) ? saved.keywords : kws;
        this.keywordsText = k.join(', ');
        this.keywordsTextSignal.set(this.keywordsText);
        this.authorBioSignal.set(this.formData?.authorBio || '');
        this.saveMessage.set({ kind: 'ok', text: 'Saved.' });
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.saveMessage.set({
          kind: 'err',
          text: err?.error?.error ?? 'Save failed.',
        });
      },
    });
  }
}
