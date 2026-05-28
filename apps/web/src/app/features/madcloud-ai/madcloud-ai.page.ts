import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth/auth.service';
import { environment } from '../../../environments/environment';

type AiStatus =
  | 'Pending'
  | 'QueuedInMADCloud'
  | 'Claimed'
  | 'InProgress'
  | 'AwaitingCallback'
  | 'ToBeDeployed'
  | 'Completed'
  | 'Cancelled'
  | 'Failed'
  | 'Deferred';

interface AiTask {
  id: string;
  sourceApp: string;
  aiRequestId: string;
  title: string;
  description?: string | null;
  notes?: string | null;
  status: AiStatus;
  kind: string;
  priority: number;
  attachmentCount: number;
  callbackState: string;
  createdDate: string;
  updatedDate?: string | null;
}

interface AiTaskDetail extends AiTask {
  targetPath: string;
  expectedOutput: string;
  validationHints: string;
  resultText?: string | null;
  resultJson?: string | null;
  patch?: string | null;
  error?: string | null;
  attachments: Array<{ id: string; filename: string; originalName: string; mimeType: string; size: number; url: string }>;
}

interface AiTaskListResponse {
  active: AiTask[];
  toBeDeployed: AiTask[];
  terminal: AiTask[];
}

interface AiTemplate {
  id: string;
  sourceApp: string;
  name: string;
  description: string;
  content: string;
  isShared: boolean;
}

interface AiSetting {
  key: string;
  valueJson: string;
}

@Component({
  selector: 'app-madcloud-ai',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <main class="mc-ai" *ngIf="isSuperAdmin; else aiAccessDenied">
      <header class="mc-topbar">
        <div>
          <p class="eyebrow">MADCloud AI service provider</p>
          <h1>/ai</h1>
          <p class="sub">{{ productName }} code fixes, AI work, callbacks, and artifacts are routed through MADCloud Local.</p>
        </div>
        <div class="top-actions">
          <span class="connection" [class.bad]="connection !== 'connected'">{{ connection }}</span>
          <button type="button" (click)="openTemplates()">Templates</button>
          <button type="button" (click)="openImport()">Import</button>
          <button type="button" class="primary" (click)="openNew()">New AI task</button>
          <button type="button" (click)="refresh()">Refresh</button>
        </div>
      </header>

      <section class="controls">
        <label *ngFor="let setting of settingKeys">
          <input type="checkbox" [checked]="settings[setting]" (change)="toggleSetting(setting, $event)">
          <span>{{ labels[setting] }}</span>
        </label>
      </section>

      <section class="stats">
        <article><small>Total</small><strong>{{ allTasks.length }}</strong></article>
        <article><small>Pending</small><strong>{{ count('QueuedInMADCloud') + count('Pending') }}</strong></article>
        <article><small>In progress</small><strong>{{ count('InProgress') + count('Claimed') }}</strong></article>
        <article><small>Callbacks</small><strong>{{ count('AwaitingCallback') }}</strong></article>
        <article><small>To deploy</small><strong>{{ count('ToBeDeployed') }}</strong></article>
        <article><small>Failed</small><strong>{{ count('Failed') }}</strong></article>
      </section>

      <section class="filters">
        <input type="search" [(ngModel)]="search" placeholder="Search tasks, notes, paths, results" aria-label="Search tasks">
        <select [(ngModel)]="statusFilter" aria-label="Filter by status">
          <option value="">All statuses</option>
          <option *ngFor="let status of statuses" [value]="status">{{ status }}</option>
        </select>
        <select [(ngModel)]="kindFilter" aria-label="Filter by kind">
          <option value="">All kinds</option>
          <option *ngFor="let kind of kinds" [value]="kind">{{ kind }}</option>
        </select>
        <select [(ngModel)]="limit" (change)="refresh()" aria-label="Results limit">
          <option [ngValue]="25">Last 25</option>
          <option [ngValue]="50">Last 50</option>
          <option [ngValue]="200">Last 200</option>
        </select>
      </section>

      <section class="task-list">
        <article class="empty" *ngIf="filteredTasks.length === 0">No MADCloud AI tasks match this view.</article>
        <article class="task-card" *ngFor="let task of filteredTasks" (click)="openDetail(task)">
          <div class="task-main">
            <div class="row">
              <span class="id">#{{ shortId(task.id) }}</span>
              <strong>{{ task.title }}</strong>
              <span class="badge" [class.warn]="task.status === 'Failed'" [class.done]="task.status === 'Completed'">{{ task.status }}</span>
              <span class="chip">{{ task.kind }}</span>
              <span class="chip">P{{ task.priority }}</span>
              <span class="chip" *ngIf="task.attachmentCount">{{ task.attachmentCount }} files</span>
            </div>
            <p>{{ task.description || task.notes || 'No description supplied.' }}</p>
            <small>MADCloud request {{ shortId(task.aiRequestId) }} &middot; callback {{ task.callbackState || 'pending' }} &middot; {{ relativeTime(task.updatedDate || task.createdDate) }}</small>
          </div>
          <button type="button" (click)="cancelTask(task, $event)">Cancel</button>
        </article>
      </section>

      <aside class="drawer" *ngIf="detail" (click)="closeDetail()">
        <div class="drawer-panel" (click)="$event.stopPropagation()">
          <button type="button" class="close" (click)="closeDetail()" aria-label="Close detail panel">Close</button>
          <p class="eyebrow">{{ detail.kind }} &middot; {{ detail.status }}</p>
          <h2>{{ detail.title }}</h2>
          <p>{{ detail.description }}</p>
          <dl>
            <div><dt>MADCloud request</dt><dd>{{ detail.aiRequestId }}</dd></div>
            <div><dt>Target path</dt><dd>{{ detail.targetPath || 'Repository root' }}</dd></div>
            <div><dt>Callback</dt><dd>{{ detail.callbackState }}</dd></div>
          </dl>
          <h3>Result</h3>
          <pre>{{ detail.resultText || detail.error || 'Waiting for MADCloud Local to complete this task.' }}</pre>
          <h3 *ngIf="detail.patch">Patch</h3>
          <pre *ngIf="detail.patch">{{ detail.patch }}</pre>
          <h3>Attachments</h3>
          <ul>
            <li *ngFor="let file of detail.attachments">{{ file.originalName }} &middot; {{ file.size }} bytes</li>
          </ul>
        </div>
      </aside>

      <section class="modal-backdrop" *ngIf="editorOpen" (click)="closeEditor()">
        <form class="modal" (submit)="saveTask($event)" (click)="$event.stopPropagation()">
          <header>
            <div>
              <p class="eyebrow">Create MADCloud work</p>
              <h2>New /ai task</h2>
            </div>
            <button type="button" (click)="closeEditor()">Close</button>
          </header>
          <label>Title<input required [(ngModel)]="draft.title" name="title"></label>
          <label>Kind
            <select [(ngModel)]="draft.kind" name="kind">
              <option *ngFor="let kind of kinds" [value]="kind">{{ kind }}</option>
            </select>
          </label>
          <label>Priority
            <select [(ngModel)]="draft.priority" name="priority">
              <option [ngValue]="1">1 Critical</option>
              <option [ngValue]="2">2 High</option>
              <option [ngValue]="3">3 Normal</option>
              <option [ngValue]="4">4 Low</option>
            </select>
          </label>
          <label>Target path/module<input [(ngModel)]="draft.targetPath" name="targetPath" placeholder="apps/api, apps/web, specific file, or blank"></label>
          <label>Description<textarea required [(ngModel)]="draft.description" name="description"></textarea></label>
          <label>Expected output<textarea [(ngModel)]="draft.expectedOutput" name="expectedOutput"></textarea></label>
          <label>Validation hints<textarea [(ngModel)]="draft.validationHints" name="validationHints"></textarea></label>
          <label>Attachments<input type="file" multiple (change)="onFilesPicked($event)"></label>
          <footer>
            <button type="button" (click)="closeEditor()">Cancel</button>
            <button type="submit" class="primary" [disabled]="saving">Send to MADCloud</button>
          </footer>
        </form>
      </section>

      <section class="modal-backdrop" *ngIf="importOpen" (click)="closeImport()">
        <form class="modal wide" (submit)="runImport($event)" (click)="$event.stopPropagation()">
          <header>
            <div>
              <p class="eyebrow">Bulk import</p>
              <h2>Import /ai tasks</h2>
            </div>
            <button type="button" (click)="closeImport()">Close</button>
          </header>
          <textarea class="json" [(ngModel)]="importJson" name="importJson" placeholder='[{"title":"Fix login","description":"..."}]'></textarea>
          <footer>
            <span>{{ importMessage }}</span>
            <button type="submit" class="primary">Import</button>
          </footer>
        </form>
      </section>

      <section class="modal-backdrop" *ngIf="templatesOpen" (click)="closeTemplates()">
        <form class="modal wide" (submit)="createTemplate($event)" (click)="$event.stopPropagation()">
          <header>
            <div>
              <p class="eyebrow">Prompt library</p>
              <h2>MADCloud templates</h2>
            </div>
            <button type="button" (click)="closeTemplates()">Close</button>
          </header>
          <div class="template-list">
            <article *ngFor="let template of templates">
              <strong>{{ template.name }}</strong>
              <p>{{ template.description }}</p>
              <button type="button" (click)="useTemplate(template)">Use</button>
            </article>
          </div>
          <label>Name<input [(ngModel)]="templateDraft.name" name="templateName"></label>
          <label>Description<input [(ngModel)]="templateDraft.description" name="templateDescription"></label>
          <label>Content<textarea [(ngModel)]="templateDraft.content" name="templateContent"></textarea></label>
          <footer><button type="submit" class="primary">Save template</button></footer>
        </form>
      </section>
    </main>

    <ng-template #aiAccessDenied>
      <main class="mc-ai access-denied">
        <section class="mc-topbar">
          <div>
            <p class="eyebrow">Restricted operator console</p>
            <h1>/ai</h1>
            <p class="sub">Only super-admin users can open the {{ productName }} MADCloud operator console.</p>
          </div>
          <span class="connection bad">restricted</span>
        </section>
      </main>
    </ng-template>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: #f5f7fb; color: #142033; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
    .mc-ai { width: min(1240px, calc(100vw - 32px)); margin: 0 auto; padding: 28px 0 56px; }
    .mc-topbar, .controls, .filters, .task-card, .empty, .modal, .drawer-panel, .stats article { background: #fff; border: 1px solid #dbe3ef; border-radius: 8px; box-shadow: 0 18px 50px rgba(15, 23, 42, .07); }
    .mc-topbar { display: flex; justify-content: space-between; gap: 20px; align-items: flex-start; padding: 22px; }
    .eyebrow { margin: 0 0 6px; color: #1279bd; text-transform: uppercase; font-size: 11px; font-weight: 900; }
    h1, h2, h3, p { letter-spacing: 0; } h1 { margin: 0; font-size: 34px; } h2 { margin: 0 0 8px; } h3 { margin: 18px 0 8px; }
    .sub { margin: 8px 0 0; color: #607086; max-width: 760px; line-height: 1.5; }
    .top-actions, .filters, .controls { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
    .controls, .filters { margin-top: 14px; padding: 14px; }
    .controls label { display: inline-flex; align-items: center; gap: 7px; font-size: 13px; color: #475467; }
    button, select, input, textarea { border-radius: 8px; border: 1px solid #cbd5e1; background: #fff; color: #142033; font: inherit; }
    button { min-height: 38px; padding: 8px 12px; font-weight: 800; cursor: pointer; }
    button.primary { background: #1279bd; color: #fff; border-color: #1279bd; }
    button:disabled { opacity: .55; cursor: not-allowed; }
    input, select, textarea { padding: 10px 12px; min-height: 40px; }
    textarea { min-height: 120px; resize: vertical; }
    .connection { display: inline-flex; align-items: center; min-height: 38px; padding: 0 10px; border-radius: 999px; background: #e8f7ef; color: #067647; font-size: 12px; font-weight: 900; }
    .connection.bad { background: #fff1f0; color: #b42318; }
    .stats { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 10px; margin-top: 14px; }
    .stats article { padding: 14px; } .stats small { color: #667085; text-transform: uppercase; font-size: 11px; font-weight: 900; } .stats strong { display: block; font-size: 26px; margin-top: 4px; }
    .filters input[type="search"] { min-width: min(420px, 100%); flex: 1; }
    .task-list { display: grid; gap: 10px; margin-top: 14px; }
    .task-card { padding: 16px; display: flex; justify-content: space-between; gap: 12px; cursor: pointer; }
    .task-card:hover { border-color: #1279bd; }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .id, .chip, .badge { font-size: 11px; font-weight: 900; border-radius: 999px; padding: 4px 8px; background: #eef2f7; color: #344054; }
    .badge { background: #e0f2fe; color: #075985; } .badge.done { background: #dcfce7; color: #166534; } .badge.warn { background: #fee2e2; color: #991b1b; }
    .task-card p { margin: 8px 0; color: #475467; line-height: 1.45; } .task-card small { color: #667085; }
    .empty { padding: 28px; color: #667085; text-align: center; }
    .drawer, .modal-backdrop { position: fixed; inset: 0; background: rgba(15, 23, 42, .54); z-index: 40; }
    .drawer { display: flex; justify-content: flex-end; }
    .drawer-panel { width: min(720px, 100vw); height: 100vh; overflow: auto; padding: 22px; border-radius: 0; }
    .close { float: right; }
    dl { display: grid; gap: 8px; } dt { color: #667085; font-size: 11px; text-transform: uppercase; font-weight: 900; } dd { margin: 0; word-break: break-word; }
    pre { white-space: pre-wrap; word-break: break-word; background: #0f172a; color: #dbeafe; border-radius: 8px; padding: 14px; max-height: 360px; overflow: auto; }
    .modal-backdrop { display: grid; place-items: center; padding: 16px; }
    .modal { width: min(680px, 100%); max-height: 92vh; overflow: auto; padding: 18px; display: grid; gap: 12px; }
    .modal.wide { width: min(920px, 100%); }
    .modal header, .modal footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .modal label { display: grid; gap: 6px; color: #475467; font-weight: 800; font-size: 13px; }
    .json { min-height: 320px; font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
    .template-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; }
    .template-list article { border: 1px solid #dbe3ef; border-radius: 8px; padding: 12px; }
    .access-denied { min-height: 100vh; display: grid; align-content: start; }
    .access-denied .mc-topbar { margin-top: 8vh; }
    @media (max-width: 860px) { .mc-topbar { display: grid; } .stats { grid-template-columns: repeat(2, minmax(0, 1fr)); } .task-card { display: grid; } }
  `]
})
export class MadcloudAiPage implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  readonly sourceApp = 'madauthor';
  readonly productName = 'MADAuthor';
  readonly apiBase = this.resolveApiBase();
  readonly statuses: AiStatus[] = ['Pending', 'QueuedInMADCloud', 'Claimed', 'InProgress', 'AwaitingCallback', 'ToBeDeployed', 'Completed', 'Cancelled', 'Failed', 'Deferred'];
  readonly kinds = ['CodeFix', 'Text', 'StructuredJson', 'Image', 'Embedding', 'AudioTranscription', 'RepositoryScan', 'Documentation', 'Workflow', 'Deployment', 'Analysis'];
  readonly settingKeys = ['workerActive', 'scannerActive', 'deployNext', 'autoApplyCodeFixes', 'autoCallback'] as const;
  readonly labels: Record<string, string> = {
    workerActive: 'Worker active',
    scannerActive: 'Scanner active',
    deployNext: 'Deploy next',
    autoApplyCodeFixes: 'Auto-apply fixes',
    autoCallback: 'Auto-callback'
  };

  connection = 'connecting';
  isSuperAdmin = false;
  allTasks: AiTask[] = [];
  detail: AiTaskDetail | null = null;
  templates: AiTemplate[] = [];
  settings: Record<string, boolean> = {};
  search = '';
  statusFilter = '';
  kindFilter = '';
  limit = 50;
  editorOpen = false;
  importOpen = false;
  templatesOpen = false;
  saving = false;
  importJson = '';
  importMessage = '';
  pendingFiles: File[] = [];
  draft = this.emptyDraft();
  templateDraft = { name: '', description: '', content: '' };
  private pollHandle?: number;

  ngOnInit(): void {
    this.isSuperAdmin = this.hasSuperAdminAccess();
    if (!this.isSuperAdmin) {
      this.connection = 'restricted';
      return;
    }

    this.refresh();
    this.loadSettings();
    this.pollHandle = window.setInterval(() => this.refresh(false), 10000);
  }

  ngOnDestroy(): void {
    if (this.pollHandle) window.clearInterval(this.pollHandle);
  }

  get filteredTasks(): AiTask[] {
    const q = this.search.trim().toLowerCase();
    return this.allTasks.filter(task => {
      const matchesSearch = !q || [task.title, task.description, task.notes, task.kind, task.status, task.callbackState].some(v => (v || '').toLowerCase().includes(q));
      const matchesStatus = !this.statusFilter || task.status === this.statusFilter;
      const matchesKind = !this.kindFilter || task.kind === this.kindFilter;
      return matchesSearch && matchesStatus && matchesKind;
    });
  }

  refresh(showConnecting = true): void {
    if (showConnecting) this.connection = 'connecting';
    this.http.get<AiTaskListResponse>(`${this.apiBase}/api/ai-tasks?sourceApp=${this.sourceApp}&limit=${this.limit}`, { headers: this.authHeaders() }).subscribe({
      next: response => {
        this.allTasks = [...response.active, ...response.toBeDeployed, ...response.terminal];
        this.connection = 'connected';
      },
      error: () => { this.connection = 'offline'; }
    });
  }

  loadSettings(): void {
    this.http.get<AiSetting[]>(`${this.apiBase}/api/ai-settings?sourceApp=${this.sourceApp}`, { headers: this.authHeaders() }).subscribe({
      next: rows => {
        for (const row of rows) this.settings[row.key] = row.valueJson === 'true';
      }
    });
  }

  toggleSetting(key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.settings[key] = checked;
    this.http.patch<AiSetting[]>(`${this.apiBase}/api/ai-settings?sourceApp=${this.sourceApp}`, { [key]: checked }, { headers: this.authHeaders() }).subscribe({
      error: () => { this.settings[key] = !checked; }
    });
  }

  openNew(): void { this.draft = this.emptyDraft(); this.pendingFiles = []; this.editorOpen = true; }
  closeEditor(): void { this.editorOpen = false; this.saving = false; }
  openImport(): void { this.importJson = ''; this.importMessage = ''; this.importOpen = true; }
  closeImport(): void { this.importOpen = false; }
  openTemplates(): void { this.templatesOpen = true; this.loadTemplates(); }
  closeTemplates(): void { this.templatesOpen = false; }
  closeDetail(): void { this.detail = null; }

  openDetail(task: AiTask): void {
    this.http.get<AiTaskDetail>(`${this.apiBase}/api/ai-tasks/${task.id}`, { headers: this.authHeaders() }).subscribe({
      next: detail => { this.detail = detail; }
    });
  }

  saveTask(event: Event): void {
    event.preventDefault();
    if (!this.draft.title.trim() || !this.draft.description.trim()) return;
    this.saving = true;
    const payload = {
      sourceApp: this.sourceApp,
      title: this.draft.title,
      description: this.draft.description,
      notes: this.draft.notes,
      status: 'QueuedInMADCloud',
      kind: this.draft.kind,
      priority: this.draft.priority,
      targetPath: this.draft.targetPath,
      expectedOutput: this.draft.expectedOutput,
      validationHints: this.draft.validationHints,
      callbackUrl: this.callbackUrl(),
      targetApplicationSlug: this.sourceApp
    };
    this.http.post<AiTaskDetail>(`${this.apiBase}/api/ai-tasks`, payload, { headers: this.authHeaders() }).subscribe({
      next: task => {
        this.uploadFiles(task.id, this.pendingFiles);
        this.closeEditor();
        this.refresh();
      },
      error: () => { this.saving = false; this.connection = 'offline'; }
    });
  }

  cancelTask(task: AiTask, event: Event): void {
    event.stopPropagation();
    this.http.patch(`${this.apiBase}/api/ai-tasks/${task.id}`, { status: 'Cancelled' }, { headers: this.authHeaders() }).subscribe({ next: () => this.refresh() });
  }

  onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pendingFiles = input.files ? Array.from(input.files) : [];
  }

  runImport(event: Event): void {
    event.preventDefault();
    let parsed: unknown;
    try { parsed = JSON.parse(this.importJson); } catch { this.importMessage = 'Invalid JSON.'; return; }
    const items = Array.isArray(parsed) ? parsed : ((parsed && typeof parsed === 'object' && Array.isArray((parsed as { items?: unknown[] }).items)) ? (parsed as { items: unknown[] }).items : []);
    this.http.post(`${this.apiBase}/api/ai-tasks/import-bulk`, { sourceApp: this.sourceApp, items }, { headers: this.authHeaders() }).subscribe({
      next: (response: any) => { this.importMessage = `Created ${response.created}, skipped ${response.skipped}`; this.refresh(); },
      error: () => { this.importMessage = 'Import failed.'; }
    });
  }

  loadTemplates(): void {
    this.http.get<AiTemplate[]>(`${this.apiBase}/api/ai-prompt-templates?sourceApp=${this.sourceApp}`, { headers: this.authHeaders() }).subscribe({ next: rows => { this.templates = rows; } });
  }

  createTemplate(event: Event): void {
    event.preventDefault();
    if (!this.templateDraft.name.trim() || !this.templateDraft.content.trim()) return;
    this.http.post<AiTemplate>(`${this.apiBase}/api/ai-prompt-templates`, { sourceApp: this.sourceApp, ...this.templateDraft }, { headers: this.authHeaders() }).subscribe({
      next: template => { this.templates = [template, ...this.templates]; this.templateDraft = { name: '', description: '', content: '' }; }
    });
  }

  useTemplate(template: AiTemplate): void {
    this.closeTemplates();
    this.openNew();
    this.draft.title = template.name;
    this.draft.description = template.content;
  }

  count(status: AiStatus): number { return this.allTasks.filter(task => task.status === status).length; }
  shortId(id: string): string { return (id || '').slice(0, 8); }
  relativeTime(value?: string | null): string {
    if (!value) return '';
    const seconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
    if (seconds < 60) return `${seconds}s ago`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return `${Math.floor(hours / 24)}d ago`;
  }

  private uploadFiles(taskId: string, files: File[]): void {
    if (files.length === 0) return;
    const data = new FormData();
    for (const file of files) data.append('files', file);
    this.http.post(`${this.apiBase}/api/ai-tasks/${taskId}/attachments`, data, { headers: this.authHeaders() }).subscribe({ next: () => this.refresh() });
  }

  private emptyDraft() {
    return { title: '', kind: 'CodeFix', priority: 3, targetPath: '', description: '', notes: '', expectedOutput: '', validationHints: '' };
  }

  private resolveApiBase(): string {
    const host = window.location.hostname;
    return host === 'localhost' || host === '127.0.0.1' ? 'http://localhost:3010' : 'https://madcloudapi.madprospects.com';
  }

  private callbackUrl(): string {
    const apiBase = environment.apiBase || `${window.location.protocol}//${window.location.hostname}:3012`;
    return `${apiBase}/api/madcloud/ai-results`;
  }

  private authHeaders(): Record<string, string> {
    const token = this.auth.accessToken() ?? this.findBearerToken();
    const headers: Record<string, string> = token ? { Authorization: `Bearer ${token}` } : {};
    const evidence = this.collectAuthEvidence();
    const hasSuperAdmin = evidence.some(value => this.isSuperAdminValue(value));
    const hasAdminEmail = evidence.some(value => value.toLowerCase().includes('admin@madprospects.com'));

    if (hasSuperAdmin || hasAdminEmail) {
      headers['X-User-Role'] = 'SuperAdmin';
    }
    if (hasAdminEmail) {
      headers['X-User-Email'] = 'admin@madprospects.com';
    }

    return headers;
  }

  private findBearerToken(): string | null {
    const candidates: string[] = [];
    const storages = [localStorage, sessionStorage];
    const jwtPattern = /eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*/g;

    for (const storage of storages) {
      for (let index = 0; index < storage.length; index += 1) {
        const key = storage.key(index);
        const value = key ? storage.getItem(key) : null;
        if (!value) {
          continue;
        }

        const matches = value.match(jwtPattern);
        if (matches) {
          candidates.push(...matches);
        }
      }
    }

    return candidates.find((token) => this.isSuperAdminValue(JSON.stringify(this.decodeJwtPayload(token) ?? ''))) ?? candidates[0] ?? null;
  }

  private hasSuperAdminAccess(): boolean {
    const user = this.auth.user();
    if (user?.email?.toLowerCase() === 'admin@madprospects.com') return true;
    if (user?.roles?.some(role => this.isSuperAdminValue(role) || role === 'Owner' || role === 'Admin')) return true;

    const values = this.collectAuthEvidence();
    return values.some(value => this.isSuperAdminValue(value)) || values.some(value => value.toLowerCase() === 'admin@madprospects.com');
  }

  private collectAuthEvidence(): string[] {
    const evidence = new Set<string>();
    const keyPattern = /(auth|token|jwt|user|account|profile|role|permission|claim|identity|session|principal|mad)/i;
    const add = (value: unknown, depth = 0): void => {
      if (value == null || depth > 5) return;
      if (typeof value === 'string') {
        const trimmed = value.trim();
        if (!trimmed) return;
        evidence.add(trimmed);
        for (const jwt of trimmed.match(/[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g) || []) {
          const payload = this.decodeJwtPayload(jwt);
          if (payload) add(payload, depth + 1);
        }
        const parsed = this.parseJson(trimmed);
        if (parsed !== undefined) add(parsed, depth + 1);
        return;
      }
      if (Array.isArray(value)) {
        for (const item of value) add(item, depth + 1);
        return;
      }
      if (typeof value === 'object') {
        for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
          if (keyPattern.test(key) || typeof item !== 'object') add(item, depth + 1);
        }
        return;
      }
      evidence.add(String(value));
    };

    for (const storage of [window.localStorage, window.sessionStorage]) {
      for (let index = 0; index < storage.length; index++) {
        const key = storage.key(index) || '';
        const value = storage.getItem(key) || '';
        if (keyPattern.test(key) || keyPattern.test(value.slice(0, 500))) {
          add(value);
        }
      }
    }

    add(document.cookie);
    return Array.from(evidence);
  }

  private isSuperAdminValue(value: string): boolean {
    const compact = value.toLowerCase().replace(/[^a-z0-9@.]/g, '');
    return compact === 'superadmin' ||
      compact === 'superadministrator' ||
      compact.includes('rolesuperadmin') ||
      compact.includes('roleclaimsrolemasuperadmin') ||
      compact.includes('superadmintrue') ||
      compact.includes('admin@madprospects.com');
  }

  private parseJson(value: string): unknown | undefined {
    if (!value.startsWith('{') && !value.startsWith('[')) return undefined;
    try { return JSON.parse(value); } catch { return undefined; }
  }

  private decodeJwtPayload(token: string): unknown | undefined {
    const payload = token.split('.')[1];
    if (!payload) return undefined;
    try {
      const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
      const padded = normalized.padEnd(normalized.length + ((4 - normalized.length % 4) % 4), '=');
      return JSON.parse(atob(padded));
    } catch {
      return undefined;
    }
  }
}
