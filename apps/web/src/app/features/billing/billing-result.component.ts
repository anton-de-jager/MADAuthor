import { Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-billing-result',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="p-8 max-w-3xl mx-auto">
      <section class="glass rounded-xl p-8">
        <p class="text-xs uppercase tracking-wider text-brand-300">Payfast checkout</p>
        <h1 class="font-display text-3xl font-semibold mt-2">{{ status === 'success' ? 'Payment received' : 'Checkout cancelled' }}</h1>
        <p class="text-ink-400 mt-3">
          {{ status === 'success'
            ? 'Payfast has returned you to MADAuthor. Subscription activation can take a moment while the notification is processed.'
            : 'No payment was completed. You can reopen Payfast checkout whenever you are ready.' }}
        </p>
        <div class="mt-6 flex flex-wrap gap-3">
          <a routerLink="/billing" class="bg-brand-600 hover:bg-brand-500 text-white rounded-lg px-4 py-2 text-sm font-medium">Back to billing</a>
          <a routerLink="/dashboard" class="border border-ink-700 hover:border-ink-500 text-ink-200 rounded-lg px-4 py-2 text-sm font-medium">Dashboard</a>
        </div>
      </section>
    </main>
  `,
})
export class BillingResultComponent {
  @Input() status: 'success' | 'cancelled' = 'success';
}
