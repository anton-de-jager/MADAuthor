import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription } from 'rxjs';
import { NotificationsService, ClaudeTaskRealtimeEvent } from '../../../core/signalr/notifications.service';
import { ToastService } from '../../../core/ui/toast.service';

// ----------------------------------------------------------------------------
// DTO shapes -- mirror MadAuthor.Contracts.ClaudeTasks.ClaudeTaskDtos.cs.
// ASP.NET serialises the ClaudeTaskStatus byte enum as a number (0-6) since no
// JsonStringEnumConverter is registered. We narrow it to a string-literal
// union in the UI via a numeric -> name map.
// ----------------------------------------------------------------------------

type StatusName =
  | 'Pending'
  | 'InProgress'
  | 'ToBeDeployed'
  | 'Completed'
  | 'Cancelled'
  | 'Failed'
  | 'Deferred';

const STATUS_BY_INDEX: StatusName[] = [
  'Pending', 'InProgress', 'ToBeDeployed', 'Completed', 'Cancelled', 'Failed', 'Deferred',
];
const INDEX_BY_STATUS: Record<StatusName, number> = {
  Pending: 0, InProgress: 1, ToBeDeployed: 2, Completed: 3, Cancelled: 4, Failed: 5, Deferred: 6,
};

interface ClaudeTaskAttachment {
  filename: string;
  originalName: string;
  mimeType: string;
  size: number;
  url: string;
}

interface ClaudeTaskSummary {
  id: number;
  title: string;
  description: string | null;
  notes: string | null;
  status: number | StatusName;
  priority: number;
  attachmentCount: number;
  createdDate: string;
  updatedDate: string | null;
}

interface ClaudeTaskDetail {
  id: number;
  title: string;
  description: string | null;
  notes: string | null;
  status: number | StatusName;
  priority: number;
  attachments: ClaudeTaskAttachment[];
  createdDate: string;
  updatedDate: string | null;
}

interface ClaudeTaskListResponse {
  active: ClaudeTaskSummary[];
  toBeDeployed: ClaudeTaskSummary[];
  terminal: ClaudeTaskSummary[];
}

interface ImportBulkResponse {
  created: number;
  skipped: number;
  createdIds: number[];
  skippedTitles: string[];
}

interface ClaudePromptTemplate {
  id: number;
  name: string;
  description: string | null;
  content: string;
  createdDate: string;
  updatedDate: string | null;
}

interface AppSetting {
  key: string;
  valueJson: string;
}

interface UiTask extends Omit<ClaudeTaskSummary, 'status'> {
  status: StatusName;
}

// ----------------------------------------------------------------------------
// Filter dropdown values
// ----------------------------------------------------------------------------

type StatusFilter =
  | ''
  | 'not_completed'
  | 'pending'
  | 'in_progress'
  | 'to_be_deployed'
  | 'completed'
  | 'cancelled'
  | 'failed'
  | 'deferred';

const STATUS_FILTERS: { label: string; value: StatusFilter }[] = [
  { label: 'All', value: '' },
  { label: 'Not completed', value: 'not_completed' },
  { label: 'Pending', value: 'pending' },
  { label: 'In progress', value: 'in_progress' },
  { label: 'To be deployed', value: 'to_be_deployed' },
  { label: 'Completed', value: 'completed' },
  { label: 'Cancelled', value: 'cancelled' },
  { label: 'Failed', value: 'failed' },
  { label: 'Deferred', value: 'deferred' },
];

const LIMIT_OPTIONS: { label: string; value: number }[] = [
  { label: '5', value: 5 },
  { label: '10', value: 10 },
  { label: '25', value: 25 },
  { label: '50', value: 50 },
  { label: 'All', value: 9999 },
];

const FILTER_TO_STATUS: Record<Exclude<StatusFilter, '' | 'not_completed'>, StatusName> = {
  pending: 'Pending',
  in_progress: 'InProgress',
  to_be_deployed: 'ToBeDeployed',
  completed: 'Completed',
  cancelled: 'Cancelled',
  failed: 'Failed',
  deferred: 'Deferred',
};

// ----------------------------------------------------------------------------
// Component
// ----------------------------------------------------------------------------

@Component({
  selector: 'app-claude-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="p-8 max-w-7xl mx-auto">
      <!-- Header -->
      <div class="flex items-start justify-between mb-6">
        <div>
          <p class="text-ink-400 text-sm">Operations</p>
          <h1 class="font-display text-3xl font-semibold tracking-tight">MAD Cloud</h1>
          <p class="text-ink-400 text-sm mt-1">Autonomous task queue. Edit, import, deploy.</p>
        </div>
        <div class="flex items-center gap-2">
          <button type="button" (click)="openTemplates()" title="Templates"
            class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md transition">
            &#9783;
          </button>
          <button type="button" (click)="openImport()" title="Import"
            class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md transition">
            &darr;
          </button>
          <button type="button" (click)="openNew()" title="New task"
            class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md transition">
            +
          </button>
          <button type="button" (click)="refresh()" title="Refresh"
            class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md transition">
            &#x21bb;
          </button>
        </div>
      </div>

      <!-- Worker toggles -->
      <div class="glass rounded-xl p-4 mb-6 flex flex-wrap items-center gap-6">
        <div class="text-xs uppercase tracking-wider text-ink-400 mr-2">Worker controls</div>
        <label class="flex items-center gap-2 cursor-pointer select-none">
          <input type="checkbox" class="sr-only peer"
            [checked]="workerActive()"
            (change)="toggleSetting('workerActive', $event)">
          <span class="w-10 h-6 rounded-full bg-ink-700 peer-checked:bg-emerald-500/80 relative transition">
            <span class="absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-ink-100 transition"
                  [class.translate-x-4]="workerActive()"></span>
          </span>
          <span class="text-sm" [class.text-emerald-300]="workerActive()">Active</span>
        </label>
        <label class="flex items-center gap-2 cursor-pointer select-none">
          <input type="checkbox" class="sr-only peer"
            [checked]="scannerActive()"
            (change)="toggleSetting('scannerActive', $event)">
          <span class="w-10 h-6 rounded-full bg-ink-700 peer-checked:bg-emerald-500/80 relative transition">
            <span class="absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-ink-100 transition"
                  [class.translate-x-4]="scannerActive()"></span>
          </span>
          <span class="text-sm" [class.text-emerald-300]="scannerActive()">Scanner</span>
        </label>
        <label class="flex items-center gap-2 cursor-pointer select-none">
          <input type="checkbox" class="sr-only peer"
            [checked]="deployNext()"
            (change)="toggleSetting('deployNext', $event)">
          <span class="w-10 h-6 rounded-full bg-ink-700 peer-checked:bg-amber-500/80 relative transition">
            <span class="absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-ink-100 transition"
                  [class.translate-x-4]="deployNext()"></span>
          </span>
          <span class="text-sm" [class.text-amber-300]="deployNext()">Deploy Next</span>
        </label>
      </div>

      <!-- Summary cards -->
      <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3 mb-6">
        <div class="glass rounded-xl p-4">
          <div class="text-xs uppercase tracking-wider text-ink-400">Total</div>
          <div class="text-2xl font-display font-semibold mt-1">{{ counts().total }}</div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-xs uppercase tracking-wider text-ink-400">In progress</div>
          <div class="text-2xl font-display font-semibold mt-1 text-sky-300">{{ counts().inProgress }}</div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-xs uppercase tracking-wider text-ink-400">Pending</div>
          <div class="text-2xl font-display font-semibold mt-1 text-amber-300">{{ counts().pending }}</div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-xs uppercase tracking-wider text-ink-400">To deploy</div>
          <div class="text-2xl font-display font-semibold mt-1 text-fuchsia-300">{{ counts().toBeDeployed }}</div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-xs uppercase tracking-wider text-ink-400">Completed</div>
          <div class="text-2xl font-display font-semibold mt-1 text-emerald-300">{{ counts().completed }}</div>
        </div>
        <div class="glass rounded-xl p-4">
          <div class="text-xs uppercase tracking-wider text-ink-400">Failed</div>
          <div class="text-2xl font-display font-semibold mt-1 text-rose-300">{{ counts().failed }}</div>
        </div>
      </div>

      <!-- Filter row -->
      <div class="flex flex-wrap items-center gap-3 mb-3">
        <label class="text-xs uppercase tracking-wider text-ink-400">Status</label>
        <select [value]="statusFilter()" (change)="setStatusFilter(asSelect($event))"
          class="bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-1.5 text-ink-100 focus:border-brand-500 focus:outline-none">
          @for (f of statusFilters; track f.value) {
            <option [value]="f.value">{{ f.label }}</option>
          }
        </select>

        <label class="text-xs uppercase tracking-wider text-ink-400 ml-3">Show last</label>
        <select [value]="terminalLimit()" (change)="setTerminalLimit(asSelect($event))"
          class="bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-1.5 text-ink-100 focus:border-brand-500 focus:outline-none">
          @for (o of limitOptions; track o.value) {
            <option [value]="o.value">{{ o.label }}</option>
          }
        </select>
        <span class="text-xs text-ink-500">completed rows</span>
      </div>

      <!-- Task list -->
      <div class="space-y-3">
        @if (filteredTasks().length === 0) {
          <div class="glass rounded-xl p-8 text-center text-ink-400">No tasks match this filter.</div>
        }
        @for (t of filteredTasks(); track t.id) {
          <div (click)="openEdit(t)"
            class="glass rounded-xl p-4 group cursor-pointer hover:border-brand-500/40 transition relative">
            <div class="flex items-start gap-3">
              <div class="text-2xl leading-none mt-0.5 select-none" [title]="t.status">{{ statusIcon(t.status) }}</div>
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-2 flex-wrap">
                  <span class="font-mono text-xs text-ink-500">#{{ t.id }}</span>
                  <span class="font-semibold truncate">{{ t.title }}</span>
                  <span [class]="statusBadgeClass(t.status)">{{ t.status }}</span>
                  @if (t.priority !== 3) {
                    <span [class]="priorityBadgeClass(t.priority)">{{ priorityLabel(t.priority) }}</span>
                  }
                  @if (t.attachmentCount > 0) {
                    <span class="text-xs text-ink-400">&#128206; {{ t.attachmentCount }}</span>
                  }
                </div>
                @if (t.description) {
                  <p class="text-sm text-ink-300 mt-1 line-clamp-2">{{ t.description }}</p>
                }
                @if (t.notes) {
                  <p class="text-xs text-ink-400 italic mt-1 line-clamp-1">{{ t.notes }}</p>
                }
              </div>
              <div class="flex items-center gap-2 flex-shrink-0 self-start">
                <span class="text-xs text-ink-500 whitespace-nowrap">{{ relativeTime(t.updatedDate || t.createdDate) }}</span>
                <button type="button" (click)="askDelete(t, $event)" title="Delete task"
                  class="opacity-0 group-hover:opacity-100 transition-opacity w-8 h-8 inline-flex items-center justify-center text-rose-400 hover:text-rose-300 border border-rose-500/30 hover:border-rose-400 rounded-md">
                  &times;
                </button>
              </div>
            </div>
          </div>
        }
      </div>
    </div>

    <!-- Edit / New modal -->
    @if (editOpen()) {
      <div class="fixed inset-0 z-40 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4"
           (click)="closeEdit()">
        <div class="glass rounded-xl p-6 max-w-xl w-full" (click)="stop($event)">
          <div class="flex items-start justify-between mb-4">
            <div>
              <p class="text-ink-400 text-xs uppercase tracking-wider">{{ editing()?.id ? 'Edit task' : 'New task' }}</p>
              <h2 class="font-display text-xl font-semibold">
                {{ editing()?.id ? '#' + editing()!.id : 'Create task' }}
              </h2>
            </div>
            <button type="button" (click)="closeEdit()" title="Close"
              class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
              &times;
            </button>
          </div>

          <form [formGroup]="taskForm" (ngSubmit)="saveTask()" class="space-y-3">
            <div>
              <label class="text-xs uppercase tracking-wider text-ink-400">Title</label>
              <input type="text" formControlName="title" maxlength="300"
                class="w-full bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none">
            </div>
            <div>
              <label class="text-xs uppercase tracking-wider text-ink-400">Description</label>
              <textarea formControlName="description"
                class="w-full min-h-32 bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none"></textarea>
            </div>
            <div>
              <label class="text-xs uppercase tracking-wider text-ink-400">Notes</label>
              <textarea formControlName="notes"
                class="w-full min-h-20 bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none"></textarea>
            </div>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="text-xs uppercase tracking-wider text-ink-400">Status</label>
                <select formControlName="status"
                  class="w-full bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none">
                  @for (s of allStatuses; track s) {
                    <option [value]="s">{{ s }}</option>
                  }
                </select>
              </div>
              <div>
                <label class="text-xs uppercase tracking-wider text-ink-400">Priority</label>
                <select formControlName="priority"
                  class="w-full bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none">
                  <option [ngValue]="1">1 - Critical</option>
                  <option [ngValue]="2">2 - High</option>
                  <option [ngValue]="3">3 - Normal</option>
                  <option [ngValue]="4">4 - Low</option>
                </select>
              </div>
            </div>

            <!-- Attachments -->
            <div>
              <label class="text-xs uppercase tracking-wider text-ink-400">Attachments</label>
              @if (editing()?.id) {
                <div class="space-y-1 mt-1">
                  @for (a of editingAttachments(); track a.filename) {
                    <div class="flex items-center justify-between text-sm bg-ink-900/40 border border-ink-700/60 rounded-md px-3 py-1.5">
                      <span class="truncate text-ink-200">{{ a.originalName }}</span>
                      <button type="button" (click)="removeAttachment(a)" title="Remove attachment"
                        class="ml-2 text-rose-400 hover:text-rose-300 text-xs">&times;</button>
                    </div>
                  }
                </div>
              } @else if (pendingFiles().length > 0) {
                <div class="space-y-1 mt-1">
                  @for (f of pendingFiles(); track f.name) {
                    <div class="flex items-center justify-between text-sm bg-ink-900/40 border border-ink-700/60 rounded-md px-3 py-1.5">
                      <span class="truncate text-ink-200">{{ f.name }}</span>
                      <span class="text-xs text-ink-500">queued</span>
                    </div>
                  }
                </div>
              }
              <input type="file" multiple (change)="onFilesPicked($event)"
                class="block mt-2 text-xs text-ink-400 file:mr-2 file:py-1 file:px-3 file:rounded-md file:border-0 file:bg-ink-800 file:text-ink-200 hover:file:bg-ink-700 cursor-pointer">
            </div>

            <div class="flex items-center justify-end gap-2 pt-2">
              <button type="button" (click)="closeEdit()" title="Cancel"
                class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
                &times;
              </button>
              <button type="submit" [disabled]="taskForm.invalid || saving()" title="Save"
                class="w-9 h-9 inline-flex items-center justify-center text-emerald-300 hover:text-emerald-200 border border-emerald-500/40 hover:border-emerald-400 rounded-md disabled:opacity-40 disabled:cursor-not-allowed">
                &#10003;
              </button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- Import modal -->
    @if (importOpen()) {
      <div class="fixed inset-0 z-40 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4"
           (click)="closeImport()">
        <div class="glass rounded-xl p-6 max-w-2xl w-full" (click)="stop($event)">
          <div class="flex items-start justify-between mb-4">
            <div>
              <p class="text-ink-400 text-xs uppercase tracking-wider">Bulk import</p>
              <h2 class="font-display text-xl font-semibold">Import tasks (JSON)</h2>
              <p class="text-ink-400 text-xs mt-1">Accepts a JSON array OR an object with an "items" key.</p>
            </div>
            <button type="button" (click)="closeImport()" title="Close"
              class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
              &times;
            </button>
          </div>

          <textarea [value]="importJson()" (input)="importJson.set(asTextarea($event))"
            class="w-full min-h-48 font-mono text-xs bg-ink-900/60 border border-ink-700 rounded-md px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none"
            placeholder='[{"title":"Fix the cover-url bug","description":"...","priority":2}]'></textarea>

          @if (importResult()) {
            <div class="mt-3 text-sm">
              <span class="text-emerald-300">Created: {{ importResult()!.created }}</span>
              <span class="mx-2 text-ink-500">,</span>
              <span class="text-amber-300">Skipped: {{ importResult()!.skipped }}</span>
            </div>
          }

          <div class="flex items-center justify-end gap-2 pt-4">
            <button type="button" (click)="loadStaticFile()" title="Load /outstanding-tasks.json"
              class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
              &darr;
            </button>
            <button type="button" (click)="closeImport()" title="Cancel"
              class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
              &times;
            </button>
            <button type="button" (click)="runImport()" [disabled]="!importJson().trim() || saving()" title="Import"
              class="w-9 h-9 inline-flex items-center justify-center text-emerald-300 hover:text-emerald-200 border border-emerald-500/40 hover:border-emerald-400 rounded-md disabled:opacity-40 disabled:cursor-not-allowed">
              &#10003;
            </button>
          </div>
        </div>
      </div>
    }

    <!-- Templates modal -->
    @if (templatesOpen()) {
      <div class="fixed inset-0 z-40 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4"
           (click)="closeTemplates()">
        <div class="glass rounded-xl p-6 max-w-2xl w-full max-h-[90vh] overflow-y-auto" (click)="stop($event)">
          <div class="flex items-start justify-between mb-4">
            <div>
              <p class="text-ink-400 text-xs uppercase tracking-wider">Prompt library</p>
              <h2 class="font-display text-xl font-semibold">Templates</h2>
            </div>
            <button type="button" (click)="closeTemplates()" title="Close"
              class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
              &times;
            </button>
          </div>

          <div class="space-y-2 mb-4">
            @if (templates().length === 0) {
              <div class="text-sm text-ink-400 italic">No templates yet.</div>
            }
            @for (tpl of templates(); track tpl.id) {
              <div class="bg-ink-900/40 border border-ink-700/60 rounded-md px-3 py-2">
                <div class="flex items-center justify-between gap-2">
                  <div class="min-w-0">
                    <div class="font-semibold truncate">{{ tpl.name }}</div>
                    @if (tpl.description) {
                      <div class="text-xs text-ink-400 truncate">{{ tpl.description }}</div>
                    }
                  </div>
                  <div class="flex items-center gap-2 flex-shrink-0">
                    <button type="button" (click)="useTemplate(tpl)" title="Use this template"
                      class="w-8 h-8 inline-flex items-center justify-center text-brand-300 hover:text-brand-200 border border-brand-500/40 hover:border-brand-400 rounded-md">
                      &rarr;
                    </button>
                    <button type="button" (click)="deleteTemplate(tpl)" title="Delete template"
                      class="w-8 h-8 inline-flex items-center justify-center text-rose-400 hover:text-rose-300 border border-rose-500/30 hover:border-rose-400 rounded-md">
                      &times;
                    </button>
                  </div>
                </div>
              </div>
            }
          </div>

          <form [formGroup]="templateForm" (ngSubmit)="createTemplate()" class="space-y-2 border-t border-ink-700/60 pt-4">
            <p class="text-xs uppercase tracking-wider text-ink-400">New template</p>
            <input type="text" formControlName="name" placeholder="Name" maxlength="200"
              class="w-full bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none">
            <input type="text" formControlName="description" placeholder="Description (optional)"
              class="w-full bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none">
            <textarea formControlName="content" placeholder="Prompt content"
              class="w-full min-h-24 bg-ink-900/60 border border-ink-700 rounded-md text-sm px-3 py-2 text-ink-100 focus:border-brand-500 focus:outline-none"></textarea>
            <div class="flex items-center justify-end">
              <button type="submit" [disabled]="templateForm.invalid || saving()" title="Create template"
                class="w-9 h-9 inline-flex items-center justify-center text-emerald-300 hover:text-emerald-200 border border-emerald-500/40 hover:border-emerald-400 rounded-md disabled:opacity-40 disabled:cursor-not-allowed">
                +
              </button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- Confirm delete modal -->
    @if (pendingDelete()) {
      <div class="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4"
           (click)="cancelDelete()">
        <div class="glass rounded-xl p-6 max-w-md w-full" (click)="stop($event)">
          <h3 class="font-display text-lg font-semibold mb-2">Delete task #{{ pendingDelete()!.id }}?</h3>
          <p class="text-sm text-ink-300 mb-4">
            "{{ pendingDelete()!.title }}". Cannot be undone.
          </p>
          <div class="flex items-center justify-end gap-2">
            <button type="button" (click)="cancelDelete()" title="Cancel"
              class="w-9 h-9 inline-flex items-center justify-center text-ink-300 hover:text-ink-100 border border-ink-700/60 hover:border-ink-500 rounded-md">
              &times;
            </button>
            <button type="button" (click)="confirmDelete()" title="Delete"
              class="w-9 h-9 inline-flex items-center justify-center text-rose-300 hover:text-rose-200 border border-rose-500/40 hover:border-rose-400 rounded-md">
              &#10003;
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ClaudePageComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private fb = inject(FormBuilder);
  private notifications = inject(NotificationsService);
  private toasts = inject(ToastService);

  // ---- Static lookups ----------------------------------------------------
  readonly statusFilters = STATUS_FILTERS;
  readonly limitOptions = LIMIT_OPTIONS;
  readonly allStatuses: StatusName[] = [
    'Pending', 'InProgress', 'ToBeDeployed', 'Completed', 'Cancelled', 'Failed', 'Deferred',
  ];

  // ---- View state --------------------------------------------------------
  allTasks = signal<UiTask[]>([]);
  statusFilter = signal<StatusFilter>('not_completed');
  terminalLimit = signal<number>(25);
  saving = signal(false);

  workerActive = signal(false);
  scannerActive = signal(false);
  deployNext = signal(false);

  // Modals
  editOpen = signal(false);
  editing = signal<ClaudeTaskDetail | null>(null);
  editingAttachments = signal<ClaudeTaskAttachment[]>([]);
  pendingFiles = signal<File[]>([]);

  importOpen = signal(false);
  importJson = signal('');
  importResult = signal<ImportBulkResponse | null>(null);

  templatesOpen = signal(false);
  templates = signal<ClaudePromptTemplate[]>([]);

  pendingDelete = signal<UiTask | null>(null);

  // ---- Forms -------------------------------------------------------------
  taskForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    description: [''],
    notes: [''],
    status: ['Pending' as StatusName],
    priority: [3],
  });

  templateForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    content: ['', Validators.required],
  });

  // ---- Derived state -----------------------------------------------------
  counts = computed(() => {
    const tasks = this.allTasks();
    return {
      total: tasks.length,
      pending: tasks.filter((t) => t.status === 'Pending').length,
      inProgress: tasks.filter((t) => t.status === 'InProgress').length,
      toBeDeployed: tasks.filter((t) => t.status === 'ToBeDeployed').length,
      completed: tasks.filter((t) => t.status === 'Completed').length,
      failed: tasks.filter((t) => t.status === 'Failed').length,
    };
  });

  filteredTasks = computed(() => {
    const filter = this.statusFilter();
    const all = this.allTasks();

    // Sort into bucket order (active -> toBeDeployed -> terminal).
    const activeStatuses: StatusName[] = ['Pending', 'InProgress', 'Deferred'];
    const terminalStatuses: StatusName[] = ['Completed', 'Cancelled', 'Failed'];
    const bucket = (t: UiTask) => {
      if (activeStatuses.includes(t.status)) return 0;
      if (t.status === 'ToBeDeployed') return 1;
      if (terminalStatuses.includes(t.status)) return 2;
      return 3;
    };

    const sorted = [...all].sort((a, b) => {
      const ba = bucket(a);
      const bb = bucket(b);
      if (ba !== bb) return ba - bb;
      // Within active/toDeploy: priority asc, id asc. Within terminal: created desc.
      if (ba === 2) return (b.updatedDate || b.createdDate).localeCompare(a.updatedDate || a.createdDate);
      if (a.priority !== b.priority) return a.priority - b.priority;
      return a.id - b.id;
    });

    let result = sorted;
    if (filter === 'not_completed') {
      result = result.filter((t) => !terminalStatuses.includes(t.status));
    } else if (filter !== '') {
      const target = FILTER_TO_STATUS[filter];
      result = result.filter((t) => t.status === target);
    }
    return result;
  });

  // ---- SignalR subscription ---------------------------------------------
  private sub?: Subscription;
  private joinedHub = false;

  async ngOnInit() {
    this.refresh();
    this.loadSettings();

    try {
      await this.notifications.joinClaudeTasks();
      this.joinedHub = true;
    } catch {
      this.toasts.push('Realtime updates unavailable. Use Refresh to reload.', 8000);
    }

    this.sub = this.notifications.claudeTaskEvents$.subscribe((ev) => this.handleRealtime(ev));
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
    if (this.joinedHub) {
      this.notifications.leaveClaudeTasks().catch(() => void 0);
    }
  }

  // ---- Data loading ------------------------------------------------------
  refresh() {
    const limit = this.terminalLimit();
    this.http
      .get<ClaudeTaskListResponse>('/api/claude-tasks', { params: { limit } })
      .subscribe({
        next: (res) => {
          const flat: UiTask[] = [
            ...res.active.map((t) => this.toUi(t)),
            ...res.toBeDeployed.map((t) => this.toUi(t)),
            ...res.terminal.map((t) => this.toUi(t)),
          ];
          this.allTasks.set(flat);
        },
        error: () => this.toasts.push('Failed to load tasks.'),
      });
  }

  private loadSettings() {
    this.http.get<AppSetting[]>('/api/settings').subscribe({
      next: (rows) => {
        const get = (k: string) => rows.find((r) => r.key === k)?.valueJson;
        this.workerActive.set(this.parseBoolJson(get('workerActive')));
        this.scannerActive.set(this.parseBoolJson(get('scannerActive')));
        this.deployNext.set(this.parseBoolJson(get('deployNext')));
      },
      error: () => this.toasts.push('Failed to load settings.'),
    });
  }

  private parseBoolJson(raw: string | undefined): boolean {
    if (!raw) return false;
    try {
      return JSON.parse(raw) === true;
    } catch {
      return raw === 'true';
    }
  }

  // ---- Realtime handling -------------------------------------------------
  private handleRealtime(ev: ClaudeTaskRealtimeEvent) {
    if (ev.type === 'task.deleted') {
      this.allTasks.update((list) => list.filter((t) => t.id !== ev.taskId));
      this.toasts.push(`Task #${ev.taskId} deleted`);
      return;
    }
    const detail = ev.task as ClaudeTaskDetail | null;
    if (!detail) return;
    const ui = this.toUi(detail);
    if (ev.type === 'task.created') {
      this.allTasks.update((list) => [ui, ...list.filter((t) => t.id !== ui.id)]);
      this.toasts.push(`Task #${ui.id} created`);
    } else if (ev.type === 'task.updated') {
      const prev = this.allTasks().find((t) => t.id === ui.id);
      this.allTasks.update((list) => list.map((t) => (t.id === ui.id ? ui : t)));
      if (prev && prev.status !== ui.status) {
        this.toasts.push(`Task #${ui.id} -> ${ui.status}`);
      }
    }
  }

  // ---- Helpers -----------------------------------------------------------
  private toUi(t: ClaudeTaskSummary | ClaudeTaskDetail): UiTask {
    return {
      id: t.id,
      title: t.title,
      description: t.description,
      notes: t.notes,
      status: this.statusName(t.status),
      priority: t.priority,
      attachmentCount: 'attachmentCount' in t ? t.attachmentCount : t.attachments.length,
      createdDate: t.createdDate,
      updatedDate: t.updatedDate,
    };
  }

  private statusName(raw: number | StatusName): StatusName {
    if (typeof raw === 'string') return raw;
    return STATUS_BY_INDEX[raw] ?? 'Pending';
  }

  statusIcon(status: StatusName): string {
    switch (status) {
      case 'Pending':       return '\u23F3';      // hourglass
      case 'InProgress':    return '\u21BB';      // clockwise arrow
      case 'ToBeDeployed':  return '\u{1F680}';  // rocket
      case 'Completed':     return '\u2713';      // check
      case 'Cancelled':     return '\u2715';      // x
      case 'Failed':        return '\u26A0';      // warning
      case 'Deferred':      return '\u23F8';      // pause
    }
  }

  statusBadgeClass(status: StatusName): string {
    const base = 'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded-full border ';
    switch (status) {
      case 'Pending':      return base + 'border-amber-500/40 text-amber-300 bg-amber-900/20';
      case 'InProgress':   return base + 'border-sky-500/40 text-sky-300 bg-sky-900/20';
      case 'ToBeDeployed': return base + 'border-fuchsia-500/40 text-fuchsia-300 bg-fuchsia-900/20';
      case 'Completed':    return base + 'border-emerald-500/40 text-emerald-300 bg-emerald-900/20';
      case 'Cancelled':    return base + 'border-ink-500/40 text-ink-400 bg-ink-900/40';
      case 'Failed':       return base + 'border-rose-500/40 text-rose-300 bg-rose-900/20';
      case 'Deferred':     return base + 'border-violet-500/40 text-violet-300 bg-violet-900/20';
    }
  }

  priorityLabel(priority: number): string {
    switch (priority) {
      case 1: return 'P1 Critical';
      case 2: return 'P2 High';
      case 4: return 'P4 Low';
      default: return `P${priority}`;
    }
  }

  priorityBadgeClass(priority: number): string {
    const base = 'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded-full border ';
    switch (priority) {
      case 1: return base + 'border-rose-500/40 text-rose-300 bg-rose-900/20';
      case 2: return base + 'border-orange-500/40 text-orange-300 bg-orange-900/20';
      case 4: return base + 'border-sky-500/40 text-sky-300 bg-sky-900/20';
      default: return base + 'border-ink-500/40 text-ink-400';
    }
  }

  relativeTime(iso: string | null | undefined): string {
    if (!iso) return '';
    const then = new Date(iso).getTime();
    if (!Number.isFinite(then)) return '';
    const s = Math.max(0, Math.floor((Date.now() - then) / 1000));
    if (s < 60) return `${s}s ago`;
    const m = Math.floor(s / 60);
    if (m < 60) return `${m} min ago`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h}h ago`;
    const d = Math.floor(h / 24);
    return `${d}d ago`;
  }

  // ---- Template helpers for event coercion ------------------------------
  asSelect(ev: Event): string {
    return (ev.target as HTMLSelectElement).value;
  }
  asTextarea(ev: Event): string {
    return (ev.target as HTMLTextAreaElement).value;
  }
  stop(ev: Event) {
    ev.stopPropagation();
  }

  // ---- Filter handlers ---------------------------------------------------
  setStatusFilter(value: string) {
    this.statusFilter.set(value as StatusFilter);
  }
  setTerminalLimit(value: string) {
    const n = parseInt(value, 10);
    this.terminalLimit.set(Number.isFinite(n) ? n : 25);
    this.refresh();
  }

  // ---- Worker settings ---------------------------------------------------
  toggleSetting(key: 'workerActive' | 'scannerActive' | 'deployNext', ev: Event) {
    const checked = (ev.target as HTMLInputElement).checked;
    // Optimistically flip the local signal so the UI feels instant.
    if (key === 'workerActive') this.workerActive.set(checked);
    if (key === 'scannerActive') this.scannerActive.set(checked);
    if (key === 'deployNext') this.deployNext.set(checked);

    this.http
      .patch(`/api/settings/${key}`, { valueJson: JSON.stringify(checked) })
      .subscribe({
        error: () => {
          // Roll back on failure.
          if (key === 'workerActive') this.workerActive.set(!checked);
          if (key === 'scannerActive') this.scannerActive.set(!checked);
          if (key === 'deployNext') this.deployNext.set(!checked);
          this.toasts.push(`Failed to update ${key}.`);
        },
      });
  }

  // ---- New / Edit task ---------------------------------------------------
  openNew() {
    this.editing.set(null);
    this.editingAttachments.set([]);
    this.pendingFiles.set([]);
    this.taskForm.reset({
      title: '',
      description: '',
      notes: '',
      status: 'Pending',
      priority: 3,
    });
    this.editOpen.set(true);
  }

  openEdit(t: UiTask) {
    // Fetch full detail (for attachments).
    this.http.get<ClaudeTaskDetail>(`/api/claude-tasks/${t.id}`).subscribe({
      next: (detail) => {
        const status = this.statusName(detail.status);
        this.editing.set({ ...detail, status });
        this.editingAttachments.set(detail.attachments ?? []);
        this.pendingFiles.set([]);
        this.taskForm.reset({
          title: detail.title,
          description: detail.description ?? '',
          notes: detail.notes ?? '',
          status,
          priority: detail.priority,
        });
        this.editOpen.set(true);
      },
      error: () => this.toasts.push('Failed to load task.'),
    });
  }

  closeEdit() {
    this.editOpen.set(false);
    this.editing.set(null);
    this.editingAttachments.set([]);
    this.pendingFiles.set([]);
  }

  onFilesPicked(ev: Event) {
    const input = ev.target as HTMLInputElement;
    if (!input.files) return;
    const next = Array.from(input.files);
    if (this.editing()?.id) {
      // Existing task: upload immediately.
      this.uploadFiles(this.editing()!.id, next);
    } else {
      // New task: queue until save.
      this.pendingFiles.update((list) => [...list, ...next]);
    }
    input.value = '';
  }

  private uploadFiles(taskId: number, files: File[]) {
    if (files.length === 0) return;
    const fd = new FormData();
    for (const f of files) fd.append('files', f);
    this.http.post<ClaudeTaskDetail>(`/api/claude-tasks/${taskId}/attachments`, fd).subscribe({
      next: (detail) => {
        this.editingAttachments.set(detail.attachments ?? []);
        this.toasts.push(`${files.length} file${files.length === 1 ? '' : 's'} uploaded`);
      },
      error: () => this.toasts.push('Upload failed.'),
    });
  }

  removeAttachment(a: ClaudeTaskAttachment) {
    const id = this.editing()?.id;
    if (!id) return;
    const encoded = encodeURIComponent(a.filename);
    this.http.delete(`/api/claude-tasks/${id}/attachments/${encoded}`).subscribe({
      next: () => {
        this.editingAttachments.update((list) => list.filter((x) => x.filename !== a.filename));
      },
      error: () => this.toasts.push('Failed to remove attachment.'),
    });
  }

  saveTask() {
    if (this.taskForm.invalid) return;
    const v = this.taskForm.getRawValue();
    const status = v.status as StatusName;
    const payload = {
      title: v.title.trim(),
      description: v.description,
      notes: v.notes,
      status: INDEX_BY_STATUS[status],
      priority: v.priority,
    };
    this.saving.set(true);
    const current = this.editing();
    if (current?.id) {
      // PATCH (terminal statuses may need override)
      const currentStatus = this.statusName(current.status);
      const movingFromTerminal =
        ['Completed', 'Cancelled', 'Failed'].includes(currentStatus) && status !== currentStatus;
      const url = `/api/claude-tasks/${current.id}` + (movingFromTerminal ? '?override=true' : '');
      this.http.patch<ClaudeTaskDetail>(url, payload).subscribe({
        next: () => {
          this.saving.set(false);
          this.closeEdit();
        },
        error: () => {
          this.saving.set(false);
          this.toasts.push('Failed to save task.');
        },
      });
    } else {
      this.http.post<ClaudeTaskDetail>('/api/claude-tasks', payload).subscribe({
        next: (created) => {
          const files = this.pendingFiles();
          if (files.length > 0) {
            const fd = new FormData();
            for (const f of files) fd.append('files', f);
            this.http.post(`/api/claude-tasks/${created.id}/attachments`, fd).subscribe({
              next: () => {
                this.saving.set(false);
                this.closeEdit();
              },
              error: () => {
                this.saving.set(false);
                this.toasts.push('Task created but attachment upload failed.');
                this.closeEdit();
              },
            });
          } else {
            this.saving.set(false);
            this.closeEdit();
          }
        },
        error: () => {
          this.saving.set(false);
          this.toasts.push('Failed to create task.');
        },
      });
    }
  }

  // ---- Delete confirm ----------------------------------------------------
  askDelete(t: UiTask, ev: Event) {
    ev.stopPropagation();
    this.pendingDelete.set(t);
  }
  cancelDelete() {
    this.pendingDelete.set(null);
  }
  confirmDelete() {
    const t = this.pendingDelete();
    if (!t) return;
    this.http.delete(`/api/claude-tasks/${t.id}`).subscribe({
      next: () => {
        this.pendingDelete.set(null);
        // Realtime handler will remove from list; if SignalR is down, fall back to direct removal.
        this.allTasks.update((list) => list.filter((x) => x.id !== t.id));
      },
      error: () => {
        this.toasts.push('Failed to delete task.');
        this.pendingDelete.set(null);
      },
    });
  }

  // ---- Import ------------------------------------------------------------
  openImport() {
    this.importJson.set('');
    this.importResult.set(null);
    this.importOpen.set(true);
  }
  closeImport() {
    this.importOpen.set(false);
  }

  loadStaticFile() {
    this.http
      .get('/outstanding-tasks.json', { responseType: 'text' })
      .subscribe({
        next: (text) => this.importJson.set(text),
        error: () => this.toasts.push('outstanding-tasks.json not found.'),
      });
  }

  runImport() {
    let parsed: unknown;
    try {
      parsed = JSON.parse(this.importJson());
    } catch {
      this.toasts.push('Invalid JSON.');
      return;
    }
    const items: unknown[] = Array.isArray(parsed)
      ? parsed
      : (parsed && typeof parsed === 'object' && Array.isArray((parsed as { items?: unknown[] }).items)
        ? (parsed as { items: unknown[] }).items
        : []);
    if (items.length === 0) {
      this.toasts.push('No items to import.');
      return;
    }
    this.saving.set(true);
    this.http
      .post<ImportBulkResponse>('/api/claude-tasks/import-bulk', { items })
      .subscribe({
        next: (res) => {
          this.saving.set(false);
          this.importResult.set(res);
          this.toasts.push(`Created: ${res.created}, Skipped: ${res.skipped}`);
        },
        error: () => {
          this.saving.set(false);
          this.toasts.push('Import failed.');
        },
      });
  }

  // ---- Templates ---------------------------------------------------------
  openTemplates() {
    this.templateForm.reset({ name: '', description: '', content: '' });
    this.loadTemplates();
    this.templatesOpen.set(true);
  }
  closeTemplates() {
    this.templatesOpen.set(false);
  }
  private loadTemplates() {
    this.http.get<ClaudePromptTemplate[]>('/api/claude-prompt-templates').subscribe({
      next: (rows) => this.templates.set(rows),
      error: () => this.toasts.push('Failed to load templates.'),
    });
  }
  createTemplate() {
    if (this.templateForm.invalid) return;
    const v = this.templateForm.getRawValue();
    this.saving.set(true);
    this.http.post<ClaudePromptTemplate>('/api/claude-prompt-templates', v).subscribe({
      next: (tpl) => {
        this.saving.set(false);
        this.templates.update((list) => [tpl, ...list]);
        this.templateForm.reset({ name: '', description: '', content: '' });
      },
      error: () => {
        this.saving.set(false);
        this.toasts.push('Failed to create template.');
      },
    });
  }
  deleteTemplate(tpl: ClaudePromptTemplate) {
    this.http.delete(`/api/claude-prompt-templates/${tpl.id}`).subscribe({
      next: () => this.templates.update((list) => list.filter((t) => t.id !== tpl.id)),
      error: () => this.toasts.push('Failed to delete template.'),
    });
  }
  useTemplate(tpl: ClaudePromptTemplate) {
    this.closeTemplates();
    this.openNew();
    this.taskForm.patchValue({
      title: tpl.name,
      description: tpl.content,
    });
  }
}
