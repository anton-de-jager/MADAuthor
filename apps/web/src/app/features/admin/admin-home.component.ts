import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';

interface HealthSummary {
  status: string;
  api: string;
  database: string;
  hangfire: string;
  pendingJobs: number;
  failedJobs: number;
  workers: number;
  utc: string;
}

@Component({
  selector: 'app-admin-home',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <main class="p-8 max-w-6xl mx-auto">
      <header class="mb-6">
        <p class="text-xs uppercase tracking-wider text-brand-300">Operations</p>
        <h1 class="font-display text-3xl font-semibold tracking-tight">Admin center</h1>
        <p class="text-ink-400 mt-2">Worker queues, MADCloud, health, Hangfire, and operator settings.</p>
      </header>

      <section class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
        <a routerLink="/admin/queue" class="glass rounded-xl p-5 hover:border-brand-500/40 transition">
          <div class="text-xs uppercase tracking-wider text-ink-400">Jobs</div>
          <div class="font-display text-lg mt-1">Worker queue</div>
          <p class="text-sm text-ink-400 mt-1">Monitor AI job queue and worker heartbeats.</p>
        </a>
        <a routerLink="/admin/ai" class="glass rounded-xl p-5 hover:border-brand-500/40 transition">
          <div class="text-xs uppercase tracking-wider text-ink-400">MADCloud</div>
          <div class="font-display text-lg mt-1">MADCloud AI</div>
          <p class="text-sm text-ink-400 mt-1">Create, import, and track MADCloud tasks.</p>
        </a>
        <a [href]="apiBase + '/hangfire'" target="_blank" rel="noopener" class="glass rounded-xl p-5 hover:border-brand-500/40 transition">
          <div class="text-xs uppercase tracking-wider text-ink-400">Background</div>
          <div class="font-display text-lg mt-1">Hangfire</div>
          <p class="text-sm text-ink-400 mt-1">Open the Admin/Owner gated dashboard.</p>
        </a>
        <a routerLink="/billing" class="glass rounded-xl p-5 hover:border-brand-500/40 transition">
          <div class="text-xs uppercase tracking-wider text-ink-400">Billing</div>
          <div class="font-display text-lg mt-1">Payfast</div>
          <p class="text-sm text-ink-400 mt-1">Review plans and checkout settings.</p>
        </a>
      </section>

      <section class="glass rounded-xl p-5">
        <div class="flex items-start justify-between gap-4 mb-4">
          <div>
            <h2 class="font-display text-xl">Health summary</h2>
            <p class="text-sm text-ink-400 mt-1">{{ apiBase || 'Current API origin' }}</p>
          </div>
          <button type="button" (click)="load()" class="border border-ink-700 hover:border-ink-500 rounded-lg px-3 py-2 text-sm">Refresh</button>
        </div>
        @if (health(); as h) {
          <div class="grid grid-cols-2 md:grid-cols-6 gap-3">
            <div class="bg-ink-900/50 rounded-lg p-3"><small>Status</small><strong>{{ h.status }}</strong></div>
            <div class="bg-ink-900/50 rounded-lg p-3"><small>API</small><strong>{{ h.api }}</strong></div>
            <div class="bg-ink-900/50 rounded-lg p-3"><small>Database</small><strong>{{ h.database }}</strong></div>
            <div class="bg-ink-900/50 rounded-lg p-3"><small>Hangfire</small><strong>{{ h.hangfire }}</strong></div>
            <div class="bg-ink-900/50 rounded-lg p-3"><small>Pending</small><strong>{{ h.pendingJobs }}</strong></div>
            <div class="bg-ink-900/50 rounded-lg p-3"><small>Failed</small><strong>{{ h.failedJobs }}</strong></div>
          </div>
        } @else {
          <div class="text-sm text-rose-300">Health summary is unavailable.</div>
        }
      </section>
    </main>
  `,
  styles: [`small { display:block; color:#94a3b8; text-transform:uppercase; font-size:.68rem; font-weight:900 } strong { display:block; margin-top:.25rem }`],
})
export class AdminHomeComponent implements OnInit {
  health = signal<HealthSummary | null>(null);
  apiBase = environment.apiBase || '';
  constructor(private http: HttpClient) {}
  ngOnInit() { this.load(); }
  load() {
    this.http.get<HealthSummary>('/api/admin/health-summary').subscribe({
      next: (h) => this.health.set(h),
      error: () => this.health.set(null),
    });
  }
}
