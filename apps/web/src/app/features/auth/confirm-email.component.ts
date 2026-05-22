import { Component, inject, Input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

type ConfirmState = 'loading' | 'success' | 'error' | 'invalid';

/**
 * Handles `/confirm-email?uid=...&token=...` links from the confirmation email. The component
 * grabs the params (delivered via `withComponentInputBinding()` in app.config.ts), POSTs to the
 * API, and renders one of three states: loading, success, error.
 */
@Component({
  selector: 'app-confirm-email',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="aurora min-h-screen flex items-center justify-center p-6">
      <div class="glass rounded-2xl w-full max-w-md p-8 text-center">
        <h1 class="font-display text-3xl font-semibold tracking-tight">
          <span class="bg-gradient-to-r from-brand-400 to-fuchsia-400 bg-clip-text text-transparent">MAD</span>Author
        </h1>

        @switch (state()) {
          @case ('loading') {
            <p class="text-ink-300 mt-6">Confirming your email…</p>
            <div class="mt-4 flex justify-center">
              <span class="inline-block h-6 w-6 rounded-full border-2 border-brand-400 border-t-transparent animate-spin"></span>
            </div>
          }
          @case ('success') {
            <p class="font-medium text-ink-100 mt-6">Email confirmed</p>
            <p class="text-ink-300 mt-1 text-sm">Your account is active. You can sign in now.</p>
            <a
              routerLink="/login"
              class="mt-6 inline-block w-full bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2.5 transition"
            >
              Go to sign in
            </a>
          }
          @case ('error') {
            <p class="font-medium text-ink-100 mt-6">Confirmation failed</p>
            <p class="text-ink-300 mt-1 text-sm">
              {{ errorMessage() }} Request a new link from the sign-in page.
            </p>
            <a
              routerLink="/login"
              class="mt-6 inline-block w-full bg-ink-800 hover:bg-ink-700 border border-ink-700 text-ink-100 font-medium rounded-lg px-4 py-2.5 transition"
            >
              Back to sign in
            </a>
          }
          @case ('invalid') {
            <p class="font-medium text-ink-100 mt-6">Invalid confirmation link</p>
            <p class="text-ink-300 mt-1 text-sm">
              This link is missing required information. Use the link from your email.
            </p>
            <a
              routerLink="/login"
              class="mt-6 inline-block w-full bg-ink-800 hover:bg-ink-700 border border-ink-700 text-ink-100 font-medium rounded-lg px-4 py-2.5 transition"
            >
              Back to sign in
            </a>
          }
        }
      </div>
    </div>
  `,
})
export class ConfirmEmailComponent implements OnInit {
  private auth = inject(AuthService);

  // Bound from `?uid=...&token=...` via withComponentInputBinding() in app.config.ts.
  @Input() uid?: string;
  @Input() token?: string;

  state = signal<ConfirmState>('loading');
  errorMessage = signal('The link is invalid or has expired.');

  ngOnInit() {
    if (!this.uid || !this.token) {
      this.state.set('invalid');
      return;
    }

    this.auth.confirmEmail(this.uid, this.token).subscribe({
      next: () => this.state.set('success'),
      error: (err) => {
        const msg = err?.error?.error;
        if (typeof msg === 'string' && msg.length > 0) this.errorMessage.set(msg);
        this.state.set('error');
      },
    });
  }
}
