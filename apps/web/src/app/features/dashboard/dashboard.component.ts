import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { BooksApi, BookSummary } from '../../core/api/books.api';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="p-8 max-w-6xl mx-auto">
      <div class="mb-8">
        <p class="text-ink-400 text-sm">Workspace</p>
        <h1 class="font-display text-3xl font-semibold tracking-tight">
          Hello, {{ firstName() }}.
        </h1>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
        <a routerLink="/books/new"
           class="glass rounded-xl p-6 hover:border-brand-500/40 hover:bg-ink-800/40 transition group">
          <div class="text-xs uppercase tracking-wider text-brand-300 mb-2">Start</div>
          <div class="text-lg font-display font-semibold mb-1">Create a new book</div>
          <div class="text-sm text-ink-400">Submit an idea, outline, manuscript, or notes.</div>
        </a>
        <a routerLink="/books"
           class="glass rounded-xl p-6 hover:border-brand-500/40 hover:bg-ink-800/40 transition">
          <div class="text-xs uppercase tracking-wider text-ink-400 mb-2">Library</div>
          <div class="text-lg font-display font-semibold mb-1">My books</div>
          <div class="text-sm text-ink-400">Everything you've started.</div>
        </a>
        <a routerLink="/books"
           class="glass rounded-xl p-6 hover:border-brand-500/40 hover:bg-ink-800/40 transition">
          <div class="text-xs uppercase tracking-wider text-fuchsia-300 mb-2">Ready</div>
          <div class="text-lg font-display font-semibold mb-1">Exports &amp; publishing</div>
          <div class="text-sm text-ink-400">PDF, EPUB, DOCX, KDP Print &amp; more - open any book to export.</div>
        </a>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <section class="glass rounded-xl p-6">
          <div class="text-xs uppercase tracking-wider text-brand-300 mb-2">Next project</div>
          @if (latestBook(); as book) {
            <h2 class="font-display text-xl font-semibold">{{ book.title }}</h2>
            <p class="text-sm text-ink-400 mt-1">{{ book.genre || 'No genre yet' }} · {{ book.completionPercentage }}% complete</p>
            <div class="mt-4 flex flex-wrap gap-2">
              <a [routerLink]="['/books', book.id]" class="bg-brand-600 hover:bg-brand-500 text-white rounded-lg px-3 py-2 text-sm">Continue</a>
              <a [routerLink]="['/books', book.id, 'exports']" class="border border-ink-700 hover:border-ink-500 rounded-lg px-3 py-2 text-sm">Open exports</a>
              <a [routerLink]="['/books', book.id, 'publishing']" class="border border-ink-700 hover:border-ink-500 rounded-lg px-3 py-2 text-sm">Publishing</a>
            </div>
          } @else {
            <p class="text-sm text-ink-400">No books yet. Start with the new-book wizard.</p>
          }
        </section>

        <section class="glass rounded-xl p-6">
          <div class="text-xs uppercase tracking-wider text-fuchsia-300 mb-2">Operations</div>
          <h2 class="font-display text-xl font-semibold">Everything is one click away</h2>
          <div class="mt-4 flex flex-wrap gap-2">
            <a routerLink="/billing" class="border border-ink-700 hover:border-ink-500 rounded-lg px-3 py-2 text-sm">Payfast billing</a>
            @if (isAdmin()) {
              <a routerLink="/admin" class="border border-ink-700 hover:border-ink-500 rounded-lg px-3 py-2 text-sm">Admin center</a>
              <a routerLink="/admin/ai" class="border border-ink-700 hover:border-ink-500 rounded-lg px-3 py-2 text-sm">MADCloud tasks</a>
            }
          </div>
        </section>
      </div>
    </div>
  `,
})
export class DashboardComponent implements OnInit {
  private auth = inject(AuthService);
  private books = inject(BooksApi);
  firstName = computed(() => this.auth.user()?.firstName ?? 'author');
  isAdmin = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Admin') || roles.includes('Owner');
  });
  latestBook = signal<BookSummary | null>(null);

  ngOnInit() {
    this.books.list().subscribe({
      next: (rows) => this.latestBook.set([...rows].sort((a, b) => b.createdDate.localeCompare(a.createdDate))[0] ?? null),
      error: () => this.latestBook.set(null),
    });
  }
}
