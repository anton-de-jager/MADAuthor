import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PayfastSubscribeComponent } from '../../shared/payfast/payfast-subscribe.component';

interface PaymentProvider {
  provider: string;
  configured: boolean;
  sandbox: boolean;
  currency: string;
  message: string;
}

@Component({
  selector: 'app-billing-page',
  standalone: true,
  imports: [CommonModule, RouterLink, PayfastSubscribeComponent],
  template: `
    <main class="p-8 max-w-6xl mx-auto">
      <a routerLink="/dashboard" class="text-sm text-ink-400 hover:text-ink-100">&larr; Dashboard</a>
      <header class="mt-4 mb-6">
        <p class="text-xs uppercase tracking-wider text-brand-300">Billing</p>
        <h1 class="font-display text-3xl font-semibold tracking-tight">Payfast plans</h1>
        <p class="text-ink-400 mt-2 max-w-3xl">
          MADAuthor uses Payfast.io only. Prices default to USD and switch to ZAR when the API detects a South African visitor.
        </p>
      </header>

      <section class="glass rounded-xl p-5 mb-6 grid grid-cols-1 md:grid-cols-4 gap-4">
        <div>
          <div class="text-xs uppercase tracking-wider text-ink-500">Provider</div>
          <div class="mt-1 text-ink-100">{{ provider()?.provider || 'Payfast' }}</div>
        </div>
        <div>
          <div class="text-xs uppercase tracking-wider text-ink-500">Currency</div>
          <div class="mt-1 text-ink-100">{{ provider()?.currency || 'USD' }}</div>
        </div>
        <div>
          <div class="text-xs uppercase tracking-wider text-ink-500">Mode</div>
          <div class="mt-1 text-ink-100">{{ provider()?.sandbox ? 'Sandbox' : 'Live' }}</div>
        </div>
        <div>
          <div class="text-xs uppercase tracking-wider text-ink-500">Status</div>
          <div class="mt-1" [class.text-emerald-300]="provider()?.configured" [class.text-amber-300]="!provider()?.configured">
            {{ provider()?.configured ? 'Configured' : 'Needs credentials' }}
          </div>
        </div>
      </section>

      <app-payfast-subscribe
        productName="MADAuthor"
        headline="Subscribe with Payfast"
        lead="Choose your MADAuthor plan and complete payment in Payfast's secure onsite checkout."
        [compact]="true"></app-payfast-subscribe>
    </main>
  `,
})
export class BillingPageComponent implements OnInit {
  provider = signal<PaymentProvider | null>(null);
  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get<PaymentProvider>('/api/payments/provider').subscribe({
      next: (provider) => this.provider.set(provider),
      error: () => this.provider.set(null),
    });
  }
}
