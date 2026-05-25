import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="aurora min-h-screen flex items-center justify-center p-6">
      <div class="glass rounded-2xl w-full max-w-md p-8">
        <img src="/logo-wide-MADAuthor.png" alt="MADAuthor" class="h-16 w-auto object-contain" />
        <p class="text-ink-400 mt-1 mb-8 text-sm">Sign in to continue building.</p>

        <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
          <label class="block">
            <span class="text-xs uppercase tracking-wider text-ink-400">Email</span>
            <input
              type="email"
              formControlName="email"
              autocomplete="email"
              class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100"
            />
          </label>
          <label class="block">
            <span class="text-xs uppercase tracking-wider text-ink-400">Password</span>
            <input
              type="password"
              formControlName="password"
              autocomplete="current-password"
              class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100"
            />
          </label>

          @if (error()) {
            <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
              {{ error() }}
            </div>
          }

          @if (unconfirmedEmail(); as unconfirmed) {
            <div class="text-sm text-ink-200 bg-ink-900/50 border border-ink-700 rounded-lg px-4 py-3">
              <p class="font-medium text-ink-100">Please confirm your email</p>
              <p class="mt-1 text-ink-300">
                We sent a confirmation link to
                <span class="text-brand-300">{{ unconfirmed }}</span>. Click it to activate your
                account, then sign in.
              </p>
              @if (resendOk()) {
                <p class="mt-2 text-emerald-300">Confirmation email re-sent. Give it a minute.</p>
              }
              @if (resendError()) {
                <p class="mt-2 text-rose-300">{{ resendError() }}</p>
              }
              <button
                type="button"
                [disabled]="resending()"
                (click)="resend(unconfirmed)"
                class="mt-3 w-full bg-ink-800 hover:bg-ink-700 border border-ink-700 text-ink-100 font-medium rounded-lg px-4 py-2 disabled:opacity-50 disabled:cursor-not-allowed transition"
              >
                {{ resending() ? 'Sending…' : 'Resend confirmation' }}
              </button>
            </div>
          }

          <button
            type="submit"
            [disabled]="form.invalid || busy()"
            class="w-full bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2.5 disabled:opacity-50 disabled:cursor-not-allowed transition"
          >
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>

          <p class="text-sm text-ink-400 text-center pt-2">
            No account?
            <a routerLink="/register" class="text-brand-300 hover:text-brand-200 ml-1">Create one</a>
          </p>
        </form>
      </div>
    </div>
  `,
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);

  busy = signal(false);
  error = signal<string | null>(null);
  /** When the API responds 403 EmailNotConfirmed, store the email so we can prompt the user. */
  unconfirmedEmail = signal<string | null>(null);
  resending = signal(false);
  resendError = signal<string | null>(null);
  resendOk = signal(false);

  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  submit() {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    this.unconfirmedEmail.set(null);
    this.resendOk.set(false);
    this.resendError.set(null);
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => {
        this.busy.set(false);
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.busy.set(false);
        const body = err?.error;
        if (err?.status === 403 && body?.error === 'EmailNotConfirmed') {
          this.unconfirmedEmail.set(body.email ?? email);
          return;
        }
        this.error.set(body?.error ?? 'Sign-in failed.');
      },
    });
  }

  resend(email: string) {
    if (this.resending()) return;
    this.resending.set(true);
    this.resendError.set(null);
    this.resendOk.set(false);
    this.auth.resendConfirmation(email).subscribe({
      next: () => {
        this.resending.set(false);
        this.resendOk.set(true);
      },
      error: () => {
        this.resending.set(false);
        this.resendError.set('Could not re-send the confirmation email. Please try again.');
      },
    });
  }
}
