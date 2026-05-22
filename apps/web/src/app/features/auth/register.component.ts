import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="aurora min-h-screen flex items-center justify-center p-6">
      <div class="glass rounded-2xl w-full max-w-md p-8">
        <h1 class="font-display text-3xl font-semibold tracking-tight">
          Create your <span class="bg-gradient-to-r from-brand-400 to-fuchsia-400 bg-clip-text text-transparent">MAD</span>Author
        </h1>
        <p class="text-ink-400 mt-1 mb-8 text-sm">Spin up a workspace in seconds. You can rename it later.</p>

        @if (confirmationSent(); as sentTo) {
          <div class="space-y-4">
            <div class="text-sm text-ink-200 bg-ink-900/50 border border-ink-700 rounded-lg px-4 py-3">
              <p class="font-medium text-ink-100">Check your inbox</p>
              <p class="mt-1 text-ink-300">
                We've sent a confirmation link to
                <span class="text-brand-300">{{ sentTo }}</span>. Click it to activate your account.
              </p>
            </div>

            @if (resendError()) {
              <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
                {{ resendError() }}
              </div>
            }
            @if (resendOk()) {
              <div class="text-sm text-emerald-300 bg-emerald-900/30 border border-emerald-700/40 rounded-md px-3 py-2">
                Confirmation email re-sent. Give it a minute.
              </div>
            }

            <button
              type="button"
              [disabled]="resending()"
              (click)="resend(sentTo)"
              class="w-full bg-ink-800 hover:bg-ink-700 border border-ink-700 text-ink-100 font-medium rounded-lg px-4 py-2.5 disabled:opacity-50 disabled:cursor-not-allowed transition"
            >
              {{ resending() ? 'Sending…' : 'Resend confirmation' }}
            </button>

            <p class="text-sm text-ink-400 text-center pt-2">
              Already confirmed?
              <a routerLink="/login" class="text-brand-300 hover:text-brand-200 ml-1">Sign in</a>
            </p>
          </div>
        } @else {
        <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-4">
          <div class="grid grid-cols-2 gap-3">
            <label class="block">
              <span class="text-xs uppercase tracking-wider text-ink-400">First name</span>
              <input formControlName="firstName" class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100" />
            </label>
            <label class="block">
              <span class="text-xs uppercase tracking-wider text-ink-400">Last name</span>
              <input formControlName="lastName" class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100" />
            </label>
          </div>
          <label class="block">
            <span class="text-xs uppercase tracking-wider text-ink-400">Email</span>
            <input type="email" formControlName="email" autocomplete="email" class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100" />
          </label>
          <label class="block">
            <span class="text-xs uppercase tracking-wider text-ink-400">Password</span>
            <input type="password" formControlName="password" autocomplete="new-password" class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100" />
            <span class="text-[11px] text-ink-500">At least 10 chars, with an uppercase letter and a digit.</span>
          </label>
          <label class="block">
            <span class="text-xs uppercase tracking-wider text-ink-400">Workspace (optional)</span>
            <input formControlName="companyName" placeholder="e.g. Mad Productions" class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5 text-ink-100" />
          </label>

          @if (error()) {
            <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
              {{ error() }}
            </div>
          }

          <button
            type="submit"
            [disabled]="form.invalid || busy()"
            class="w-full bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2.5 disabled:opacity-50 disabled:cursor-not-allowed transition"
          >
            {{ busy() ? 'Creating…' : 'Create account' }}
          </button>

          <p class="text-sm text-ink-400 text-center pt-2">
            Have an account?
            <a routerLink="/login" class="text-brand-300 hover:text-brand-200 ml-1">Sign in</a>
          </p>
        </form>
        }
      </div>
    </div>
  `,
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);

  busy = signal(false);
  error = signal<string | null>(null);

  /** When set, the user has submitted Register successfully and an email was dispatched. */
  confirmationSent = signal<string | null>(null);
  resending = signal(false);
  resendError = signal<string | null>(null);
  resendOk = signal(false);

  form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10)]],
    companyName: [''],
  });

  submit() {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    const payload = this.form.getRawValue();
    this.auth.register(payload).subscribe({
      next: (res) => {
        this.busy.set(false);
        this.confirmationSent.set(res.email ?? payload.email);
      },
      error: (err) => {
        this.busy.set(false);
        const e = err?.error;
        this.error.set(
          (Array.isArray(e?.errors) ? e.errors.join(' ') : e?.error) ??
            'Could not create the account.',
        );
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
