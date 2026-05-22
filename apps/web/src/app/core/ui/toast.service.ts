import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  text: string;
  /** ms to display; null = sticky (must be dismissed manually). */
  durationMs: number | null;
}

/**
 * Lightweight signal-backed toast service. Components push toasts via `push(text)`,
 * `ToastsComponent` (mounted once in the shell) renders them top-right and auto-dismisses
 * after `durationMs`. Used for SignalR milestone events ("Sipho freshly drafted chapter 4")
 * so the user feels a live team narrating progress.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  push(text: string, durationMs: number | null = 6000): void {
    const id = this.nextId++;
    this.toasts.update((list) => [...list, { id, text, durationMs }]);
    if (durationMs !== null) {
      setTimeout(() => this.dismiss(id), durationMs);
    }
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
