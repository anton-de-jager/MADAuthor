import { CommonModule } from '@angular/common';
import { Component, Input, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../environments/environment';

interface PayfastPlan {
  code: string;
  name: string;
  currency: 'USD' | 'ZAR';
  amount: number;
  cadence: string;
  badge: string;
  features: string[];
}

interface PayfastSession {
  uuid?: string;
  engineUrl?: string;
  configured?: boolean;
  message?: string;
}

declare global {
  interface Window {
    payfast_do_onsite_payment?: (options: { uuid: string; return_url?: string; cancel_url?: string }, callback?: (result: boolean) => void) => void;
  }
}

@Component({
  selector: 'app-payfast-subscribe',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="payfast-subscribe" [class.payfast-subscribe--compact]="compact" aria-label="Payfast subscription">
      <div class="payfast-subscribe__copy">
        <p class="payfast-subscribe__eyebrow">Payfast secure checkout</p>
        <h2>{{ headline || ('Start ' + productName + ' today') }}</h2>
        <p class="payfast-subscribe__lead">{{ lead || 'Choose a plan, add your email, and subscribe without leaving the page.' }}</p>
        <div class="payfast-subscribe__trust" aria-label="Payment trust points">
          <span>Payfast onsite modal</span>
          <span>No card details stored here</span>
          <span>{{ currencyLabel }}</span>
        </div>
      </div>

      <form class="payfast-subscribe__panel" (ngSubmit)="subscribe(selectedPlanCode)">
        <label class="payfast-subscribe__email">
          <span>Email for receipt</span>
          <input name="payfast-email" type="email" [(ngModel)]="email" autocomplete="email" placeholder="you@company.com">
        </label>

        <div class="payfast-subscribe__plans">
          <button
            *ngFor="let plan of plans"
            class="payfast-subscribe__plan"
            [class.is-active]="plan.code === selectedPlanCode"
            type="button"
            (click)="selectedPlanCode = plan.code"
          >
            <span class="payfast-subscribe__badge">{{ plan.badge }}</span>
            <strong>{{ plan.name }}</strong>
            <span class="payfast-subscribe__price">{{ format(plan) }}</span>
            <small>{{ plan.features[0] }}</small>
          </button>
        </div>

        <button class="payfast-subscribe__cta" type="submit" [disabled]="busy">
          {{ busy ? 'Opening Payfast...' : ('Subscribe to ' + selectedPlanName) }}
        </button>
        <p class="payfast-subscribe__status" [class.is-error]="statusTone === 'error'" [class.is-success]="statusTone === 'success'" *ngIf="statusMessage">
          {{ statusMessage }}
        </p>
      </form>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .payfast-subscribe {
      --payfast-primary: var(--brand-primary, var(--primary, #1279bd));
      --payfast-secondary: var(--brand-secondary, var(--secondary, #00c2ff));
      display: grid;
      grid-template-columns: minmax(0, 0.95fr) minmax(320px, 1.05fr);
      gap: clamp(1rem, 3vw, 2rem);
      align-items: stretch;
      margin: clamp(2rem, 6vw, 5rem) auto;
      padding: clamp(1rem, 4vw, 2rem);
      border: 1px solid color-mix(in srgb, var(--payfast-primary) 24%, rgba(255,255,255,0.2));
      border-radius: 8px;
      background:
        radial-gradient(circle at 12% 0%, color-mix(in srgb, var(--payfast-secondary) 28%, transparent) 0, transparent 30%),
        linear-gradient(135deg, color-mix(in srgb, #ffffff 92%, var(--payfast-secondary)), color-mix(in srgb, #ffffff 88%, var(--payfast-primary)));
      box-shadow: 0 24px 60px rgba(12, 22, 38, 0.14);
      color: #101827;
      overflow: hidden;
    }
    .payfast-subscribe--compact { margin-block: 1.5rem; }
    .payfast-subscribe__copy { display: flex; flex-direction: column; justify-content: center; min-width: 0; }
    .payfast-subscribe__eyebrow {
      margin: 0 0 0.75rem;
      color: var(--payfast-primary);
      font-size: 0.78rem;
      font-weight: 800;
      text-transform: uppercase;
    }
    .payfast-subscribe h2 {
      margin: 0;
      max-width: 12ch;
      font-size: clamp(2rem, 4vw, 4rem);
      line-height: 0.98;
      letter-spacing: 0;
    }
    .payfast-subscribe__lead {
      margin: 1rem 0 0;
      max-width: 48rem;
      color: #435069;
      font-size: 1rem;
      line-height: 1.65;
    }
    .payfast-subscribe__trust {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-top: 1.25rem;
    }
    .payfast-subscribe__trust span {
      border: 1px solid color-mix(in srgb, var(--payfast-primary) 22%, #dfe7f2);
      border-radius: 999px;
      padding: 0.45rem 0.7rem;
      background: rgba(255,255,255,0.72);
      color: #233047;
      font-size: 0.82rem;
      font-weight: 700;
    }
    .payfast-subscribe__panel {
      display: grid;
      gap: 0.85rem;
      min-width: 0;
      border-radius: 8px;
      background: rgba(255,255,255,0.82);
      padding: clamp(1rem, 3vw, 1.25rem);
      box-shadow: inset 0 0 0 1px rgba(255,255,255,0.55);
      backdrop-filter: blur(12px);
    }
    .payfast-subscribe__email { display: grid; gap: 0.4rem; color: #2f3a51; font-weight: 800; }
    .payfast-subscribe__email input {
      width: 100%;
      min-height: 48px;
      border: 1px solid #d8e1ed;
      border-radius: 8px;
      padding: 0 0.9rem;
      background: #fff;
      color: #101827;
      font: inherit;
    }
    .payfast-subscribe__plans {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 0.6rem;
    }
    .payfast-subscribe__plan {
      display: grid;
      gap: 0.35rem;
      min-width: 0;
      min-height: 132px;
      border: 1px solid #d8e1ed;
      border-radius: 8px;
      padding: 0.8rem;
      background: #fff;
      color: #101827;
      text-align: left;
      cursor: pointer;
      transition: border-color 160ms ease, transform 160ms ease, box-shadow 160ms ease;
    }
    .payfast-subscribe__plan:hover,
    .payfast-subscribe__plan.is-active {
      border-color: var(--payfast-primary);
      box-shadow: 0 12px 30px color-mix(in srgb, var(--payfast-primary) 18%, transparent);
      transform: translateY(-1px);
    }
    .payfast-subscribe__badge { color: var(--payfast-primary); font-size: 0.72rem; font-weight: 900; text-transform: uppercase; }
    .payfast-subscribe__price { font-size: clamp(1.4rem, 3vw, 2rem); font-weight: 900; line-height: 1; }
    .payfast-subscribe__plan small { color: #5a6880; line-height: 1.35; }
    .payfast-subscribe__cta {
      min-height: 52px;
      border: 0;
      border-radius: 8px;
      padding: 0 1rem;
      background: linear-gradient(135deg, var(--payfast-primary), var(--payfast-secondary));
      color: #fff;
      font: inherit;
      font-weight: 900;
      cursor: pointer;
      box-shadow: 0 16px 32px color-mix(in srgb, var(--payfast-primary) 24%, transparent);
    }
    .payfast-subscribe__cta:disabled { cursor: wait; opacity: 0.72; }
    .payfast-subscribe__status { margin: 0; color: #40506b; font-weight: 700; line-height: 1.45; }
    .payfast-subscribe__status.is-error { color: #b42318; }
    .payfast-subscribe__status.is-success { color: #027a48; }
    @media (max-width: 860px) {
      .payfast-subscribe { grid-template-columns: 1fr; }
      .payfast-subscribe h2 { max-width: 100%; font-size: 2.25rem; }
      .payfast-subscribe__plans { grid-template-columns: 1fr; }
      .payfast-subscribe__plan { min-height: 0; }
    }
  `],
})
export class PayfastSubscribeComponent implements OnInit {
  @Input() productName = 'MAD Prospects';
  @Input() headline = '';
  @Input() lead = '';
  @Input() compact = false;

  email = '';
  busy = false;
  statusMessage = '';
  statusTone: 'neutral' | 'success' | 'error' = 'neutral';
  selectedPlanCode = 'growth';
  paymentBase = '';
  plans: PayfastPlan[] = [
    { code: 'starter', name: 'Starter', currency: 'USD', amount: 19, cadence: '/mo', badge: 'Start quickly', features: ['Single workspace'] },
    { code: 'growth', name: 'Growth', currency: 'USD', amount: 49, cadence: '/mo', badge: 'Most popular', features: ['Team workspace'] },
    { code: 'scale', name: 'Scale', currency: 'USD', amount: 149, cadence: '/mo', badge: 'For growing teams', features: ['Advanced operations'] },
  ];

  get selectedPlanName(): string {
    return this.plans.find((plan) => plan.code === this.selectedPlanCode)?.name ?? 'Growth';
  }

  get currencyLabel(): string {
    const currency = this.plans[0]?.currency ?? 'USD';
    return currency === 'ZAR' ? 'ZAR for South African visitors' : 'USD by default';
  }

  async ngOnInit(): Promise<void> {
    await this.loadPlans();
  }

  async subscribe(planCode: string): Promise<void> {
    this.busy = true;
    this.statusTone = 'neutral';
    this.statusMessage = 'Creating a secure Payfast checkout session...';

    try {
      if (!this.paymentBase) {
        await this.loadPlans();
      }

      const response = await fetch(`${this.paymentBase}/payfast/onsite`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          planCode,
          email: this.email || undefined,
          returnUrl: `${window.location.origin}/billing/success`,
          cancelUrl: `${window.location.origin}/billing/cancelled`,
        }),
      });
      const session = (await response.json()) as PayfastSession;

      if (!response.ok || !session.uuid) {
        throw new Error(session.message || 'Payfast is not ready yet.');
      }

      await this.loadPayfastEngine(session.engineUrl || 'https://www.payfast.co.za/onsite/engine.js');
      const launcher = window.payfast_do_onsite_payment;
      if (!launcher) {
        throw new Error('Payfast checkout could not be loaded.');
      }

      this.statusMessage = 'Payfast is open. Complete payment in the secure window.';
      launcher({ uuid: session.uuid }, (result) => {
        this.statusTone = result ? 'success' : 'neutral';
        this.statusMessage = result ? 'Payment complete. Your subscription is being activated.' : 'Payfast checkout was closed before completion.';
      });
    } catch (error) {
      this.statusTone = 'error';
      this.statusMessage = error instanceof Error ? error.message : 'Could not start Payfast checkout.';
    } finally {
      this.busy = false;
    }
  }

  format(plan: PayfastPlan): string {
    const symbol = plan.currency === 'ZAR' ? 'R' : '$';
    return `${symbol}${Math.round(plan.amount)}${plan.cadence}`;
  }

  private async loadPlans(): Promise<void> {
    for (const base of this.paymentCandidates()) {
      try {
        const response = await fetch(`${base}/plans`, { headers: { Accept: 'application/json' } });
        if (!response.ok) continue;
        const plans = (await response.json()) as PayfastPlan[];
        if (Array.isArray(plans) && plans.length) {
          this.plans = plans;
          this.paymentBase = base;
          this.statusMessage = '';
          return;
        }
      } catch {
        // Try the next likely API route.
      }
    }

    this.paymentBase = this.paymentCandidates()[0];
    this.statusTone = 'neutral';
    this.statusMessage = 'Payfast plans are loading from the API; fallback pricing is shown for now.';
  }

  private async loadPayfastEngine(src: string): Promise<void> {
    if (window.payfast_do_onsite_payment) return;
    await new Promise<void>((resolve, reject) => {
      const existing = document.querySelector<HTMLScriptElement>(`script[src="${src}"]`);
      if (existing) {
        existing.addEventListener('load', () => resolve(), { once: true });
        existing.addEventListener('error', () => reject(new Error('Payfast checkout script failed to load.')), { once: true });
        return;
      }

      const script = document.createElement('script');
      script.src = src;
      script.async = true;
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('Payfast checkout script failed to load.'));
      document.head.appendChild(script);
    });
  }

  private paymentCandidates(): string[] {
    const paths = ['/api/payments', '/v1/payments', '/payments'];
    const host = window.location.hostname.toLowerCase();
    const protocol = window.location.protocol;
    const candidates: string[] = [];
    if (environment.apiBase) {
      paths.forEach((path) => candidates.push(`${environment.apiBase}${path}`));
    }
    const match = host.match(/^([a-z0-9-]+)\.madprospects\.com$/);

    if (match && !match[1].endsWith('api')) {
      const apiHost = `${match[1]}api.madprospects.com`;
      paths.forEach((path) => candidates.push(`${protocol}//${apiHost}${path}`));
    }

    paths.forEach((path) => candidates.push(path));
    return Array.from(new Set(candidates));
  }
}
