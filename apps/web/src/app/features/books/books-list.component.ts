import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BooksApi, BookSummary, STAGE_LABELS, STATUS_LABELS } from '../../core/api/books.api';
import { DatePipe } from '@angular/common';

@Component({
  selector: 'app-books-list',
  standalone: true,
  imports: [RouterLink, DatePipe],
  template: `
    <div class="p-8 max-w-6xl mx-auto">
      <div class="flex items-center justify-between mb-6">
        <div>
          <p class="text-ink-400 text-sm">Library</p>
          <h1 class="font-display text-3xl font-semibold tracking-tight">My books</h1>
        </div>
        <a routerLink="/books/new"
           class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2.5 transition">
          + Create book
        </a>
      </div>

      @if (loading()) {
        <div class="glass rounded-xl p-12 text-center text-ink-400">Loading…</div>
      } @else if (books().length === 0) {
        <div class="glass rounded-xl p-12 text-center">
          <div class="text-2xl mb-2">📚</div>
          <div class="font-display text-xl mb-1">No books yet</div>
          <div class="text-ink-400 text-sm mb-6">Drop an idea or upload notes — the worker takes it from there.</div>
          <a routerLink="/books/new"
             class="inline-block bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2.5 transition">
            Create your first book
          </a>
        </div>
      } @else {
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          @for (book of books(); track book.id) {
            <a [routerLink]="['/books', book.id]"
               class="glass rounded-xl p-5 hover:border-brand-500/40 hover:bg-ink-800/40 transition block">
              <div class="flex items-start justify-between mb-2">
                <span class="text-xs uppercase tracking-wider text-brand-300">{{ stageLabel(book) }}</span>
                <span class="text-xs text-ink-500">{{ book.createdDate | date:'mediumDate' }}</span>
              </div>
              <div class="font-display text-lg font-semibold mb-1">{{ book.title }}</div>
              @if (book.subtitle) {
                <div class="text-sm text-ink-400 mb-3">{{ book.subtitle }}</div>
              }
              <div class="flex items-center justify-between mt-4">
                <span class="text-xs text-ink-400">{{ statusLabel(book) }}</span>
                <div class="text-xs text-ink-400">{{ book.completionPercentage }}%</div>
              </div>
              <div class="mt-2 h-1 rounded-full bg-ink-800 overflow-hidden">
                <div class="h-full bg-gradient-to-r from-brand-500 to-fuchsia-500"
                     [style.width.%]="book.completionPercentage"></div>
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
})
export class BooksListComponent implements OnInit {
  private api = inject(BooksApi);

  loading = signal(true);
  books = signal<BookSummary[]>([]);

  ngOnInit() {
    this.api.list().subscribe({
      next: (list) => {
        this.books.set(list);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  statusLabel(b: BookSummary) {
    return STATUS_LABELS[b.status] ?? b.status;
  }

  stageLabel(b: BookSummary) {
    return STAGE_LABELS[b.workflowStage] ?? b.workflowStage;
  }
}
