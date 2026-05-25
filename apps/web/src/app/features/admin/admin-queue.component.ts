import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';

interface JobRow {
  id: string;
  bookProjectId: string;
  jobType: string;
  status: string;
  priority: number;
  stage?: string | null;
  progress: number;
  retryCount: number;
  maxRetries: number;
  claimedBy?: string | null;
  claimedAt?: string | null;
  startedDate?: string | null;
  completedDate?: string | null;
  errorMessage?: string | null;
  createdDate: string;
}

interface HeartbeatRow {
  workerId: string;
  lastPing: string;
  lastJobId?: string | null;
  ageSeconds: number;
}

@Component({
  selector: 'app-admin-queue',
  standalone: true,
  imports: [CommonModule, DatePipe, RouterLink],
  template: `
    <div class="p-8 max-w-7xl mx-auto">
      <div class="mb-6">
        <p class="text-ink-400 text-sm">Admin</p>
        <h1 class="font-display text-3xl font-semibold tracking-tight">Worker queue</h1>
      </div>

      <!-- Heartbeats -->
      <section class="mb-8">
        <h2 class="font-display text-xl mb-3">Worker heartbeats</h2>
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
          @if (heartbeats().length === 0) {
            <div class="glass rounded-xl p-5 text-ink-400 col-span-full">No heartbeats yet - the worker hasn't run.</div>
          }
          @for (h of heartbeats(); track h.workerId) {
            <div class="glass rounded-xl p-4">
              <div class="flex items-center justify-between">
                <div class="text-xs uppercase tracking-wider text-ink-400">{{ h.workerId }}</div>
                <span [class]="
                  'text-xs px-2 py-0.5 rounded-full ' +
                  (h.ageSeconds < 90
                    ? 'bg-emerald-900/40 text-emerald-300'
                    : h.ageSeconds < 600
                      ? 'bg-amber-900/40 text-amber-300'
                      : 'bg-rose-900/40 text-rose-300')
                ">
                  {{ ageLabel(h.ageSeconds) }}
                </span>
              </div>
              <div class="text-sm text-ink-200 mt-1">Last ping: {{ h.lastPing | date: 'mediumTime' }}</div>
              @if (h.lastJobId) {
                <div class="text-xs text-ink-500 mt-1 truncate">Last job: {{ h.lastJobId }}</div>
              }
            </div>
          }
        </div>
      </section>

      <!-- Filters -->
      <section>
        <div class="flex flex-wrap items-center gap-2 mb-3">
          <h2 class="font-display text-xl mr-3">Jobs</h2>
          @for (f of filters; track f.value) {
            <button (click)="setFilter(f.value)"
              [class]="
                'text-xs px-3 py-1 rounded-full border ' +
                (filter() === f.value
                  ? 'bg-brand-600/20 border-brand-500/50 text-brand-200'
                  : 'border-ink-700 text-ink-400 hover:text-ink-100 hover:border-ink-500')
              ">
              {{ f.label }}
            </button>
          }
          <button (click)="refresh()" class="text-xs px-3 py-1 rounded-full border border-ink-700 text-ink-400 hover:text-ink-100 hover:border-ink-500 ml-auto">
            Refresh
          </button>
        </div>

        <div class="glass rounded-xl overflow-hidden">
          <table class="w-full text-sm">
            <thead class="bg-ink-900/50 text-xs uppercase tracking-wider text-ink-400">
              <tr>
                <th class="text-left px-4 py-3">Job</th>
                <th class="text-left px-4 py-3">Project</th>
                <th class="text-left px-4 py-3">Status</th>
                <th class="text-left px-4 py-3">Stage</th>
                <th class="text-right px-4 py-3">Progress</th>
                <th class="text-left px-4 py-3">Created</th>
                <th class="text-right px-4 py-3"></th>
              </tr>
            </thead>
            <tbody>
              @if (jobs().length === 0) {
                <tr><td colspan="7" class="px-4 py-8 text-center text-ink-400">No jobs match.</td></tr>
              }
              @for (j of jobs(); track j.id) {
                <tr class="border-t border-ink-800/70">
                  <td class="px-4 py-3 font-mono text-xs">
                    <div>{{ j.jobType }}</div>
                    <div class="text-[10px] text-ink-500">{{ j.id.substring(0, 8) }}…</div>
                  </td>
                  <td class="px-4 py-3">
                    <a [routerLink]="['/books', j.bookProjectId]" class="text-brand-300 hover:text-brand-200 font-mono text-xs">
                      {{ j.bookProjectId.substring(0, 8) }}…
                    </a>
                  </td>
                  <td class="px-4 py-3">
                    <span [class]="statusClass(j.status)">{{ j.status }}</span>
                    @if (j.retryCount > 0) {
                      <span class="text-[10px] text-ink-500 ml-2">retry {{ j.retryCount }}/{{ j.maxRetries }}</span>
                    }
                  </td>
                  <td class="px-4 py-3 text-ink-300">{{ j.stage || '-' }}</td>
                  <td class="px-4 py-3 text-right">{{ j.progress }}%</td>
                  <td class="px-4 py-3 text-ink-400 text-xs">{{ j.createdDate | date: 'mediumTime' }}</td>
                  <td class="px-4 py-3 text-right">
                    @if (j.status === 'Failed') {
                      <button (click)="retry(j.id)" class="text-xs text-brand-300 hover:text-brand-200">Retry</button>
                    }
                  </td>
                </tr>
                @if (j.errorMessage) {
                  <tr class="border-t border-rose-900/30 bg-rose-900/10">
                    <td colspan="7" class="px-4 py-2 text-xs text-rose-300">{{ j.errorMessage }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
      </section>
    </div>
  `,
})
export class AdminQueueComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);

  filters = [
    { label: 'All', value: '' },
    { label: 'Pending', value: 'Pending' },
    { label: 'Claimed', value: 'Claimed' },
    { label: 'InProgress', value: 'InProgress' },
    { label: 'Completed', value: 'Completed' },
    { label: 'Failed', value: 'Failed' },
  ];

  filter = signal('');
  jobs = signal<JobRow[]>([]);
  heartbeats = signal<HeartbeatRow[]>([]);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit() {
    this.refresh();
    this.timer = setInterval(() => this.refresh(), 4000);
  }

  ngOnDestroy() {
    if (this.timer) clearInterval(this.timer);
  }

  setFilter(value: string) {
    this.filter.set(value);
    this.refresh();
  }

  refresh() {
    const params = this.filter() ? `?status=${this.filter()}` : '';
    this.http.get<JobRow[]>('/api/admin/jobs' + params).subscribe((rows) => this.jobs.set(rows));
    this.http.get<HeartbeatRow[]>('/api/admin/heartbeats').subscribe((rows) => this.heartbeats.set(rows));
  }

  retry(jobId: string) {
    this.http.post(`/api/admin/jobs/${jobId}/retry`, {}).subscribe(() => this.refresh());
  }

  statusClass(status: string) {
    switch (status) {
      case 'Pending':    return 'text-ink-300';
      case 'Claimed':
      case 'InProgress': return 'text-amber-300';
      case 'Completed':  return 'text-emerald-400';
      case 'Failed':     return 'text-rose-300';
      case 'Cancelled':  return 'text-ink-500';
      default:           return 'text-ink-300';
    }
  }

  ageLabel(s: number) {
    if (s < 60) return `${s}s ago`;
    if (s < 3600) return `${Math.floor(s / 60)}m ago`;
    if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
    return `${Math.floor(s / 86400)}d ago`;
  }
}
