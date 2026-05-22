import { Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

/**
 * Visual layer for ToastService. Mounted once near the root of the shell so toasts
 * float above every route. Stacked top-right, fade-in via Tailwind animation utility.
 */
@Component({
  selector: 'app-toasts',
  standalone: true,
  template: `
    <div class="fixed top-4 right-4 z-50 flex flex-col gap-2 max-w-sm pointer-events-none">
      @for (t of toast.toasts(); track t.id) {
        <div
          class="pointer-events-auto bg-ink-900/95 border border-brand-500/30 text-ink-100 text-sm rounded-lg shadow-xl shadow-black/40 px-4 py-3 backdrop-blur flex items-start gap-3 animate-toast-in">
          <span class="text-brand-300 mt-0.5">●</span>
          <span class="flex-1 leading-snug">{{ t.text }}</span>
          <button
            type="button"
            (click)="toast.dismiss(t.id)"
            class="text-ink-500 hover:text-ink-200 text-xs leading-none mt-0.5"
            aria-label="Dismiss">✕</button>
        </div>
      }
    </div>
  `,
  styles: [
    `
    @keyframes toast-in {
      from { opacity: 0; transform: translateY(-8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
    .animate-toast-in {
      animation: toast-in 180ms ease-out;
    }
    `,
  ],
})
export class ToastsComponent {
  toast = inject(ToastService);
}
