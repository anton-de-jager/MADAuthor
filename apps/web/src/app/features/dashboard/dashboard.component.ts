import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

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

      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
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
          <div class="text-sm text-ink-400">PDF, EPUB, DOCX, KDP Print &amp; more — open any book to export.</div>
        </a>
      </div>
    </div>
  `,
})
export class DashboardComponent {
  private auth = inject(AuthService);
  firstName = computed(() => this.auth.user()?.firstName ?? 'author');
}
