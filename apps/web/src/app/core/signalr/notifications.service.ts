import { Injectable, inject, OnDestroy } from '@angular/core';
import {
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject, Observable } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { environment } from '../../../environments/environment';

export interface JobProgressEvent {
  jobId: string;
  bookProjectId: string;
  jobType: string;
  // status is now humanized server-side via HumanVoice — "In progress", "Done", "Paused" etc.
  status: string;
  stage: string | null;
  progress: number;
  errorMessage: string | null;
  /** Set only when a job has just completed; a short, persona-flavoured toast string. */
  milestoneToast: string | null;
}

/** Realtime event broadcast by the API on every claude-task mutation. Mirrors the
 *  server-side `MadAuthor.Contracts.ClaudeTasks.ClaudeTaskEvent` record. */
export interface ClaudeTaskRealtimeEvent {
  type: 'task.created' | 'task.updated' | 'task.deleted';
  taskId: number;
  /** null on `task.deleted`. */
  task: unknown | null;
}

@Injectable({ providedIn: 'root' })
export class NotificationsService implements OnDestroy {
  private auth = inject(AuthService);
  private connection?: HubConnection;
  private startPromise?: Promise<void>;
  private readonly _jobProgress$ = new Subject<JobProgressEvent>();
  private readonly _claudeTaskEvents$ = new Subject<ClaudeTaskRealtimeEvent>();
  private joinedGroups = new Set<string>();
  /** Tracked separately from project groups -- it's a single global group with no id. */
  private joinedClaudeTasks = false;

  readonly jobProgress$: Observable<JobProgressEvent> = this._jobProgress$.asObservable();
  readonly claudeTaskEvents$: Observable<ClaudeTaskRealtimeEvent> = this._claudeTaskEvents$.asObservable();

  /** Idempotent connect. Resolves once the hub is in Connected state. */
  async ensureConnected(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;
    if (this.startPromise) return this.startPromise;

    // Lock to WebSockets only — if the SignalR negotiate call falls back to long-polling
    // (which is what was producing the "lots of empty WebSocket-shaped HTTP calls" pattern
    // in the network tab), we want to FAIL LOUD rather than silently degrade. If you ever
    // see this connection fail in prod, the fix is enabling the IIS WebSocket Protocol
    // feature on the API site in Plesk (Web Hosting Settings -> WebSocket protocol).
    this.connection = new HubConnectionBuilder()
      .withUrl(`${environment.apiBase}/hubs/notifications`, {
        accessTokenFactory: () => this.auth.accessToken() ?? '',
        withCredentials: true,
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      // Persistent reconnect — the default policy gives up after 4 attempts (≈30s),
      // which means an API restart or any blip > 30s leaves the page silently
      // disconnected until the user refreshes. Retry forever with backoff so the
      // connection self-heals.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => {
          // 0, 2s, 5s, 10s, 30s, then 60s forever.
          const delays = [0, 2000, 5000, 10000, 30000];
          return delays[ctx.previousRetryCount] ?? 60000;
        },
      })
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('JobProgress', (msg: JobProgressEvent) => this._jobProgress$.next(msg));
    this.connection.on('ClaudeTaskEvent', (msg: ClaudeTaskRealtimeEvent) => this._claudeTaskEvents$.next(msg));

    this.connection.onreconnected(async () => {
      // Re-join previously subscribed project groups after a reconnect.
      for (const id of this.joinedGroups) {
        try { await this.connection!.invoke('JoinProjectGroup', id); } catch { /* ignore */ }
      }
      // Re-join the claude-tasks global group if we were previously subscribed.
      if (this.joinedClaudeTasks) {
        try { await this.connection!.invoke('JoinClaudeTasksGroup'); } catch { /* ignore */ }
      }
    });

    this.startPromise = this.connection.start();
    try {
      await this.startPromise;
    } finally {
      this.startPromise = undefined;
    }
  }

  async joinProject(projectId: string): Promise<void> {
    await this.ensureConnected();
    if (this.joinedGroups.has(projectId)) return;
    await this.connection!.invoke('JoinProjectGroup', projectId);
    this.joinedGroups.add(projectId);
  }

  async leaveProject(projectId: string): Promise<void> {
    if (!this.joinedGroups.has(projectId)) return;
    try { await this.connection?.invoke('LeaveProjectGroup', projectId); } catch { /* ignore */ }
    this.joinedGroups.delete(projectId);
  }

  /** Subscribe to the operator /claude task event stream. Admin/Owner role required
   *  server-side; the hub throws if the connection lacks the role. */
  async joinClaudeTasks(): Promise<void> {
    await this.ensureConnected();
    if (this.joinedClaudeTasks) return;
    await this.connection!.invoke('JoinClaudeTasksGroup');
    this.joinedClaudeTasks = true;
  }

  async leaveClaudeTasks(): Promise<void> {
    if (!this.joinedClaudeTasks) return;
    try { await this.connection?.invoke('LeaveClaudeTasksGroup'); } catch { /* ignore */ }
    this.joinedClaudeTasks = false;
  }

  ngOnDestroy() {
    this.connection?.stop().catch(() => void 0);
  }
}
