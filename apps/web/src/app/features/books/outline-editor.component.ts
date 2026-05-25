import { Component, EventEmitter, Input, OnChanges, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import {
  BookChapterSummary,
  BooksApi,
  OutlineChapterPayload,
} from '../../core/api/books.api';

/**
 * In-place editor for a planned chapter outline.
 *
 * Drives a local list of rows the user can rename, reorder, add, or delete.
 * Order is maintained client-side; the server is the source of truth for
 * ChapterNumber after Save (it renormalizes to 1..N based on the order we
 * send). Save is only enabled when the local state diverges from the
 * snapshot we last loaded from the parent.
 */
interface EditableRow {
  /** Stable client-side key used for @for tracking. Survives reorder. */
  key: string;
  /** Server id - null for newly-added rows that haven't been saved yet. */
  id: string | null;
  title: string;
  summary: string;
}

@Component({
  selector: 'app-outline-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="glass rounded-xl p-5 border-ink-700">
      <div class="flex items-center justify-between mb-4 gap-4">
        <div>
          <h2 class="font-display text-xl">Edit outline</h2>
          <p class="text-xs text-ink-400 mt-1">
            Rename, reorder, add, or delete chapters before approval. Chapter numbers
            are recomputed when you save.
          </p>
        </div>
        <div class="text-xs text-ink-500 whitespace-nowrap">
          {{ rows().length }} chapter(s)
        </div>
      </div>

      @if (rows().length === 0) {
        <div class="border border-dashed border-ink-700 rounded-lg px-4 py-6 text-sm text-ink-400 text-center mb-4">
          No chapters yet. Click "Add chapter" below to add the first row.
        </div>
      }

      <ul class="space-y-3 mb-4">
        @for (row of rows(); track row.key; let i = $index) {
          <li class="border border-ink-700 rounded-lg p-3 bg-ink-900/40">
            <div class="flex items-start gap-3">
              <!-- Reorder + position -->
              <div class="flex flex-col items-center gap-1 pt-1">
                <div class="text-[10px] uppercase tracking-wider text-ink-500">#{{ i + 1 }}</div>
                <button type="button" (click)="moveUp(i)" [disabled]="i === 0"
                  class="w-7 h-6 rounded bg-ink-800 hover:bg-ink-700 disabled:opacity-30 text-ink-200 text-xs"
                  aria-label="Move chapter up">&uarr;</button>
                <button type="button" (click)="moveDown(i)" [disabled]="i === rows().length - 1"
                  class="w-7 h-6 rounded bg-ink-800 hover:bg-ink-700 disabled:opacity-30 text-ink-200 text-xs"
                  aria-label="Move chapter down">&darr;</button>
              </div>

              <!-- Fields -->
              <div class="flex-1 min-w-0">
                <label class="block text-[11px] uppercase tracking-wider text-ink-500 mb-1">Title</label>
                <input
                  type="text"
                  [ngModel]="row.title"
                  (ngModelChange)="updateTitle(i, $event)"
                  [name]="'title-' + row.key"
                  placeholder="Chapter title"
                  class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm text-ink-100" />

                <label class="block text-[11px] uppercase tracking-wider text-ink-500 mt-3 mb-1">Summary</label>
                <textarea
                  [ngModel]="row.summary"
                  (ngModelChange)="updateSummary(i, $event)"
                  [name]="'summary-' + row.key"
                  rows="2"
                  placeholder="What this chapter covers (optional)"
                  class="w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2 text-sm text-ink-100"></textarea>
              </div>

              <!-- Delete -->
              <button type="button" (click)="remove(i)"
                class="self-start text-xs text-ink-400 hover:text-rose-300 border border-ink-700 hover:border-rose-500/40 rounded px-2 py-1"
                aria-label="Delete chapter">
                Delete
              </button>
            </div>
          </li>
        }
      </ul>

      <div class="flex flex-wrap items-center justify-between gap-3">
        <button type="button" (click)="add()"
          class="text-sm border border-dashed border-ink-700 hover:border-brand-500/50 text-ink-300 hover:text-ink-100 rounded-lg px-4 py-2">
          + Add chapter
        </button>

        <div class="flex items-center gap-2">
          @if (saveMessage(); as msg) {
            <span class="text-xs"
              [class.text-emerald-300]="msg.kind === 'ok'"
              [class.text-rose-300]="msg.kind === 'err'">
              {{ msg.text }}
            </span>
          }
          <button type="button" (click)="cancel()" [disabled]="saving()"
            class="text-sm border border-ink-700 hover:border-ink-500 text-ink-200 rounded-lg px-4 py-2 disabled:opacity-50">
            Cancel
          </button>
          <button type="button" (click)="save()" [disabled]="saving() || !canSave()"
            class="text-sm bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 disabled:opacity-40 text-white font-medium rounded-lg px-4 py-2">
            {{ saving() ? 'Saving…' : 'Save outline' }}
          </button>
        </div>
      </div>

      @if (validationError(); as err) {
        <div class="mt-3 text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
          {{ err }}
        </div>
      }
    </div>
  `,
})
export class OutlineEditorComponent implements OnChanges {
  @Input() projectId!: string;
  @Input() chapters: BookChapterSummary[] = [];

  /** Emitted after a successful save so the parent can refresh BookDetail. */
  @Output() saved = new EventEmitter<BookChapterSummary[]>();
  /** Emitted when the user clicks Cancel and the editor should close. */
  @Output() cancelled = new EventEmitter<void>();

  private api = inject(BooksApi);

  rows = signal<EditableRow[]>([]);
  saving = signal(false);
  saveMessage = signal<{ kind: 'ok' | 'err'; text: string } | null>(null);
  validationError = signal<string | null>(null);

  /** Snapshot of the rows as they were when last loaded. Used for dirty check. */
  private snapshot: EditableRow[] = [];
  private keyCounter = 0;

  /** Save is only enabled when there's a real diff vs. the snapshot. */
  canSave = computed(() => this.isDirty(this.rows()));

  ngOnChanges() {
    this.reset();
  }

  private reset() {
    const rows = (this.chapters ?? []).map((c) => this.toRow(c));
    this.snapshot = rows.map((r) => ({ ...r }));
    this.rows.set(rows);
    this.saveMessage.set(null);
    this.validationError.set(null);
  }

  private toRow(c: BookChapterSummary): EditableRow {
    return {
      key: `db-${c.id}`,
      id: c.id,
      title: c.title ?? '',
      summary: c.summary ?? '',
    };
  }

  private isDirty(current: EditableRow[]): boolean {
    if (current.length !== this.snapshot.length) return true;
    for (let i = 0; i < current.length; i++) {
      const a = current[i];
      const b = this.snapshot[i];
      if (a.id !== b.id) return true;
      if ((a.title ?? '') !== (b.title ?? '')) return true;
      if ((a.summary ?? '') !== (b.summary ?? '')) return true;
    }
    return false;
  }

  updateTitle(index: number, value: string) {
    this.rows.update((rs) => {
      const next = rs.slice();
      next[index] = { ...next[index], title: value };
      return next;
    });
  }

  updateSummary(index: number, value: string) {
    this.rows.update((rs) => {
      const next = rs.slice();
      next[index] = { ...next[index], summary: value };
      return next;
    });
  }

  moveUp(index: number) {
    if (index <= 0) return;
    this.rows.update((rs) => {
      const next = rs.slice();
      const tmp = next[index - 1];
      next[index - 1] = next[index];
      next[index] = tmp;
      return next;
    });
  }

  moveDown(index: number) {
    this.rows.update((rs) => {
      if (index >= rs.length - 1) return rs;
      const next = rs.slice();
      const tmp = next[index + 1];
      next[index + 1] = next[index];
      next[index] = tmp;
      return next;
    });
  }

  add() {
    this.rows.update((rs) => [
      ...rs,
      {
        key: `new-${++this.keyCounter}`,
        id: null,
        title: '',
        summary: '',
      },
    ]);
  }

  remove(index: number) {
    const row = this.rows()[index];
    if (!row) return;
    // Only prompt for confirmation when there's meaningful content to lose.
    const hasContent = (row.title?.trim() || row.summary?.trim());
    if (hasContent && !confirm(`Delete "${row.title || 'untitled chapter'}"?`)) return;
    this.rows.update((rs) => rs.filter((_, i) => i !== index));
  }

  cancel() {
    this.reset();
    this.cancelled.emit();
  }

  save() {
    const current = this.rows();
    const trimmed = current.map((r) => ({
      ...r,
      title: (r.title ?? '').trim(),
      summary: (r.summary ?? '').trim(),
    }));

    if (trimmed.length === 0) {
      this.validationError.set('At least one chapter is required before saving.');
      return;
    }
    if (trimmed.some((r) => !r.title)) {
      this.validationError.set('Every chapter needs a title.');
      return;
    }
    this.validationError.set(null);

    const payload: OutlineChapterPayload[] = trimmed.map((r, i) => ({
      id: r.id ?? undefined,
      chapterNumber: i + 1,
      title: r.title,
      summary: r.summary ? r.summary : null,
    }));

    this.saving.set(true);
    this.saveMessage.set(null);
    this.api.updateOutline(this.projectId, payload).subscribe({
      next: (updated) => {
        this.saving.set(false);
        // Rebuild the snapshot from the server response so the dirty check
        // resets correctly (new rows now have server-assigned ids).
        const fresh = (updated ?? []).map((c) => this.toRow(c));
        this.snapshot = fresh.map((r) => ({ ...r }));
        this.rows.set(fresh);
        this.saveMessage.set({ kind: 'ok', text: 'Outline saved.' });
        this.saved.emit(updated ?? []);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const msg = err?.error?.error ?? 'Save failed. Please try again.';
        this.saveMessage.set({ kind: 'err', text: msg });
      },
    });
  }
}
