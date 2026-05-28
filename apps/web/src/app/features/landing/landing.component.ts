import { Component, computed, inject, signal } from '@angular/core';
import { PayfastSubscribeComponent } from '../../shared/payfast/payfast-subscribe.component';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

const MAD_UNIVERSE_APPS = [
  { name: 'MAD Prospects', url: 'https://madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-madprospects.png' },
  { name: 'MADai', url: 'https://madai.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADai.png' },
  { name: 'MADAuthor', url: 'https://madauthor.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADAuthor.png' },
  { name: 'MAD Cloud', url: 'https://madcloud.madprospects.com/', logo: '' },
  { name: 'MADCreate', url: 'https://madcreate.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADCreate.png' },
  { name: 'MADHub', url: 'https://madhub.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADHub.png' },
  { name: 'MADLeads', url: 'https://madleads.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADLeads.png' },
  { name: 'MADLearn', url: 'https://madlearn.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADLearn.png' },
  { name: 'MADLove', url: 'https://madlove.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADLove.png' },
  { name: 'MADMultisciple', url: 'https://madmultisciple.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADMultisciple.png' },
  { name: 'MADPulse', url: 'https://madpulse.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADPulse.png' },
  { name: 'MADRecruiting', url: 'https://madrecruiting.madprospects.com/', logo: 'https://madprospects.com/media/logo-wide-MADRecruiting.png' },
] as const;

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [RouterLink, PayfastSubscribeComponent],
  template: `
    <div class="aurora min-h-screen text-ink-100 overflow-x-hidden">
      <!-- ===== Top bar ===== -->
      <header class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 py-6 flex items-center justify-between">
        <a routerLink="/home" class="inline-flex items-center">
          <img src="/logo-wide-MADAuthor.png" alt="MADAuthor" class="h-12 w-auto object-contain" />
        </a>
        <nav class="flex items-center gap-3 text-sm">
          <a href="#pricing" class="text-ink-300 hover:text-ink-100 transition px-3 py-2">Pricing</a>
          @if (signedIn()) {
            <a routerLink="/dashboard"
               class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 transition shadow-lg shadow-brand-900/40">
              Open dashboard →
            </a>
          } @else {
            <a routerLink="/login" class="text-ink-300 hover:text-ink-100 transition px-3 py-2">Sign in</a>
            <a routerLink="/register"
               class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 transition shadow-lg shadow-brand-900/40">
              Start free
            </a>
          }
        </nav>
      </header>

      <!-- ===== Hero ===== -->
      <section class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 pt-12 lg:pt-20 pb-16 lg:pb-24">
        <div class="grid lg:grid-cols-12 gap-12 items-center">
          <div class="lg:col-span-7">
            <div class="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-brand-500/10 border border-brand-500/30 text-xs uppercase tracking-wider text-brand-200 mb-6">
              <span class="w-1.5 h-1.5 rounded-full bg-brand-400 animate-pulse"></span>
              Idea to published book · automated
            </div>
            <h1 class="font-display text-5xl lg:text-7xl font-semibold tracking-tight leading-[1.02]">
              Write a book
              <br />
              <span class="bg-gradient-to-r from-brand-300 via-fuchsia-400 to-rose-300 bg-clip-text text-transparent animate-[shimmer_8s_ease-in-out_infinite] bg-[length:200%_auto]">
                this weekend.
              </span>
            </h1>
            <p class="mt-6 text-lg lg:text-xl text-ink-300 max-w-2xl leading-relaxed">
              MADAuthor is the AI publishing studio for people who'd rather <em>have</em> written a book.
              Hand it an idea. It plans, drafts, edits, illustrates the cover, and exports the manuscript
              to every format Amazon and Ingram accept &mdash; while you watch chapters land in real time.
            </p>

            <div class="mt-10 flex flex-wrap items-center gap-4">
              @if (signedIn()) {
                <a routerLink="/books/new"
                   class="group relative inline-flex items-center gap-2 bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-xl px-7 py-4 text-lg transition shadow-2xl shadow-brand-900/50">
                  Start a new book
                  <span class="transition-transform group-hover:translate-x-1">→</span>
                  <span class="absolute inset-0 rounded-xl ring-2 ring-brand-400/0 group-hover:ring-brand-400/40 transition"></span>
                </a>
                <a routerLink="/books" class="text-ink-300 hover:text-ink-100 transition px-4 py-3">
                  Or see your library
                </a>
              } @else {
                <a routerLink="/register"
                   class="group relative inline-flex items-center gap-2 bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-xl px-7 py-4 text-lg transition shadow-2xl shadow-brand-900/50">
                  Start writing my book
                  <span class="transition-transform group-hover:translate-x-1">→</span>
                  <span class="absolute inset-0 rounded-xl ring-2 ring-brand-400/0 group-hover:ring-brand-400/40 transition"></span>
                </a>
                <a routerLink="/login" class="text-ink-300 hover:text-ink-100 transition px-4 py-3">
                  I've been here before
                </a>
              }
            </div>

            <!-- Trust strip -->
            <div class="mt-12 flex flex-wrap items-center gap-x-8 gap-y-3 text-xs text-ink-400">
              <div class="flex items-center gap-2">
                <span class="text-emerald-400">●</span> 7 export formats (PDF · EPUB · DOCX · KDP Print · Ingram · HTML · Markdown)
              </div>
              <div class="flex items-center gap-2">
                <span class="text-emerald-400">●</span> AI cover &amp; metadata
              </div>
              <div class="flex items-center gap-2">
                <span class="text-emerald-400">●</span> Multi-language
              </div>
            </div>
          </div>

          <!-- Hero card stack -->
          <div class="lg:col-span-5 relative">
            <div class="relative h-[440px]">
              <!-- back card -->
              <div class="absolute inset-0 glass rounded-2xl p-5 rotate-3 translate-x-6 translate-y-6 opacity-60">
                <div class="text-xs uppercase tracking-wider text-brand-300 mb-2">Chapter 14</div>
                <div class="font-display text-lg font-semibold mb-1">Becoming Managing Director</div>
                <div class="text-xs text-ink-400">Drafted · 4,879 words</div>
              </div>
              <!-- mid card -->
              <div class="absolute inset-0 glass rounded-2xl p-5 -rotate-2 translate-x-3 translate-y-3 opacity-80">
                <div class="text-xs uppercase tracking-wider text-fuchsia-300 mb-2">Chapter 9</div>
                <div class="font-display text-lg font-semibold mb-1">Learning the Numbers</div>
                <div class="text-xs text-ink-400">Edited · 6,655 words</div>
              </div>
              <!-- front card -->
              <div class="absolute inset-0 glass rounded-2xl p-5">
                <div class="flex items-center justify-between mb-4">
                  <div>
                    <div class="text-xs uppercase tracking-wider text-brand-300">Publishing · Ready</div>
                    <div class="font-display text-2xl font-semibold tracking-tight mt-1">The Apprentice's Compass</div>
                    <div class="text-sm text-ink-400">From Boilermaker to Manager</div>
                  </div>
                  <div class="text-3xl">📘</div>
                </div>
                <div class="space-y-1 mt-4">
                  <div class="flex justify-between text-xs text-ink-400">
                    <span>Progress</span><span>100%</span>
                  </div>
                  <div class="h-1.5 rounded-full bg-ink-800 overflow-hidden">
                    <div class="h-full bg-gradient-to-r from-brand-500 to-fuchsia-500 w-full"></div>
                  </div>
                </div>
                <div class="grid grid-cols-3 gap-2 mt-5 text-xs">
                  <div class="bg-ink-900/60 border border-ink-800 rounded-md px-2 py-1.5 text-center text-ink-300">PDF</div>
                  <div class="bg-ink-900/60 border border-ink-800 rounded-md px-2 py-1.5 text-center text-ink-300">EPUB</div>
                  <div class="bg-ink-900/60 border border-ink-800 rounded-md px-2 py-1.5 text-center text-ink-300">DOCX</div>
                  <div class="bg-fuchsia-900/30 border border-fuchsia-700/40 rounded-md px-2 py-1.5 text-center text-fuchsia-200">KDP Print</div>
                  <div class="bg-fuchsia-900/30 border border-fuchsia-700/40 rounded-md px-2 py-1.5 text-center text-fuchsia-200">Ingram</div>
                  <div class="bg-ink-900/60 border border-ink-800 rounded-md px-2 py-1.5 text-center text-ink-300">+2 more</div>
                </div>
                <div class="mt-5 pt-4 border-t border-ink-800 flex items-center justify-between text-xs">
                  <span class="inline-flex items-center gap-1.5 text-emerald-400">
                    <span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span> live · 15 chapters · 83,085 words
                  </span>
                  <span class="text-ink-500">≈ 4 days</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="mad-universe-strip" aria-label="Explore the MAD universe">
        <div class="mad-universe-inner">
          <p class="mad-universe-kicker"><span aria-hidden="true">*</span> The MAD universe</p>
          <div class="mad-universe-marquee">
            <div class="mad-universe-track">
              @for (app of madUniverseApps.concat(madUniverseApps); track app.name + $index) {
                <a class="mad-universe-link" [href]="app.url" target="_blank" rel="noopener" [attr.aria-label]="app.name">
                  @if (app.logo) {
                    <img [src]="app.logo" [alt]="app.name" loading="lazy" decoding="async" />
                  } @else {
                    <span class="mad-universe-text">{{ app.name }}</span>
                  }
                </a>
              }
            </div>
          </div>
        </div>
      </section>

      <!-- ===== Pipeline ===== -->
      <section class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 py-16 lg:py-24">
        <div class="text-center mb-12">
          <div class="text-xs uppercase tracking-wider text-brand-300 mb-3">The Pipeline</div>
          <h2 class="font-display text-3xl lg:text-5xl font-semibold tracking-tight">
            Seven AI agents. One finished book.
          </h2>
          <p class="mt-4 text-ink-400 max-w-2xl mx-auto">
            Each step hands off to the next automatically. No prompts to babysit, no copy-pasting between tools.
          </p>
        </div>

        <div class="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
          @for (step of pipeline; track step.label) {
            <div class="glass rounded-xl p-5 hover:border-brand-500/40 transition group">
              <div class="text-2xl mb-3">{{ step.icon }}</div>
              <div class="font-display text-lg font-semibold">{{ step.label }}</div>
              <div class="text-sm text-ink-400 mt-1">{{ step.copy }}</div>
            </div>
          }
        </div>
      </section>

      <!-- ===== Why ===== -->
      <section class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 py-16 lg:py-24">
        <div class="grid lg:grid-cols-3 gap-6">
          <div class="glass rounded-2xl p-7">
            <div class="text-3xl mb-3">⚡</div>
            <div class="font-display text-xl font-semibold mb-2">Hours, not months</div>
            <p class="text-ink-400 text-sm leading-relaxed">
              A book that would take you nine months of weekends lands in your library
              over a long weekend. Real chapters. Real voice. Real word counts.
            </p>
          </div>
          <div class="glass rounded-2xl p-7">
            <div class="text-3xl mb-3">🎨</div>
            <div class="font-display text-xl font-semibold mb-2">Cover &amp; metadata, done</div>
            <p class="text-ink-400 text-sm leading-relaxed">
              Cover art, KDP description, 7 keywords, 3 BISAC codes, launch emails,
              social posts &mdash; generated alongside the manuscript, on-brand and ready to paste.
            </p>
          </div>
          <div class="glass rounded-2xl p-7">
            <div class="text-3xl mb-3">📦</div>
            <div class="font-display text-xl font-semibold mb-2">Upload-ready exports</div>
            <p class="text-ink-400 text-sm leading-relaxed">
              KDP-spec interior PDF, Ingram-spec PDF with 0.125&Prime; bleed, EPUB,
              DOCX, plain HTML, plain Markdown. Drag onto KDP &amp; ship.
            </p>
          </div>
        </div>
      </section>

      <!-- ===== Pricing ===== -->
      <section id="pricing" class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 py-16 lg:py-24 scroll-mt-24">
        <div class="text-center mb-12">
          <div class="text-xs uppercase tracking-wider text-brand-300 mb-3">Pricing</div>
          <h2 class="font-display text-3xl lg:text-5xl font-semibold tracking-tight">
            One book, one weekend, one price.
          </h2>
          <p class="mt-4 text-ink-400 max-w-2xl mx-auto">
            Pick the plan that fits how often you publish. Upgrade, downgrade, or cancel any time.
          </p>

          <!-- Monthly / Yearly toggle -->
          <div class="mt-8 inline-flex items-center gap-2 p-1 rounded-full bg-ink-900/60 border border-ink-800">
            <button type="button"
                    (click)="billing.set('monthly')"
                    [class]="'px-5 py-2 rounded-full text-sm font-medium transition hover:text-ink-100 ' + (billing() === 'monthly' ? 'bg-gradient-to-r from-brand-600 to-fuchsia-600 text-white shadow-lg shadow-brand-900/40' : 'text-ink-300')">
              Monthly
            </button>
            <button type="button"
                    (click)="billing.set('yearly')"
                    [class]="'px-5 py-2 rounded-full text-sm font-medium transition hover:text-ink-100 inline-flex items-center gap-2 ' + (billing() === 'yearly' ? 'bg-gradient-to-r from-brand-600 to-fuchsia-600 text-white shadow-lg shadow-brand-900/40' : 'text-ink-300')">
              Yearly
              <span class="text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded-full bg-emerald-500/15 text-emerald-300 border border-emerald-500/30">2 months free</span>
            </button>
          </div>
        </div>

        <div class="grid lg:grid-cols-3 gap-6 items-start">
          @for (plan of plans; track plan.id) {
            <div [class]="'glass rounded-2xl p-7 relative hover:-translate-y-1 transition flex flex-col ' + (plan.featured ? 'lg:-mt-4 lg:pb-10 ring-2 ring-brand-500 bg-gradient-to-br from-brand-600/10 to-fuchsia-600/10' : '')">
              @if (plan.featured) {
                <div class="absolute -top-3 left-1/2 -translate-x-1/2">
                  <span class="inline-flex items-center gap-1 text-[11px] uppercase tracking-wider px-3 py-1 rounded-full bg-gradient-to-r from-brand-600 to-fuchsia-600 text-white shadow-lg shadow-brand-900/40">
                    Most popular <span class="text-fuchsia-200">✦</span>
                  </span>
                </div>
              }

              <div class="font-display text-2xl font-semibold tracking-tight">{{ plan.name }}</div>
              <div class="text-sm text-ink-400 mt-1 min-h-[2.5rem]">{{ plan.tagline }}</div>

              <div class="mt-6 flex items-baseline gap-1">
                <span class="font-display text-5xl font-semibold tracking-tight">\${{ billing() === 'yearly' ? plan.yearly : plan.monthly }}</span>
                <span class="text-ink-400 text-sm">/{{ billing() === 'yearly' ? 'yr' : 'mo' }}</span>
              </div>
              @if (billing() === 'yearly') {
                <div class="mt-2 inline-flex items-center self-start text-[11px] uppercase tracking-wider px-2 py-1 rounded-full bg-emerald-500/15 text-emerald-300 border border-emerald-500/30">
                  Save \${{ (plan.monthly * 12) - plan.yearly }}/yr
                </div>
              } @else {
                <div class="mt-2 h-[26px]"></div>
              }

              <ul class="mt-6 space-y-2.5 text-sm flex-1">
                @for (feature of plan.features; track feature.label) {
                  <li class="flex items-start gap-2">
                    @if (feature.value === true) {
                      <span class="text-emerald-400 mt-0.5">✓</span>
                      <span class="text-ink-200">{{ feature.label }}</span>
                    } @else if (feature.value === false) {
                      <span class="text-ink-600 mt-0.5">—</span>
                      <span class="text-ink-500 line-through">{{ feature.label }}</span>
                    } @else {
                      <span class="text-emerald-400 mt-0.5">✓</span>
                      <span class="text-ink-200">
                        {{ feature.label }}
                        <span class="text-brand-300">· {{ feature.value }}</span>
                      </span>
                    }
                  </li>
                }
              </ul>

              <div class="mt-7">
                @if (signedIn()) {
                  <a routerLink="/dashboard"
                     [class]="'block text-center font-medium rounded-xl px-5 py-3 transition ' + (plan.featured ? 'bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white shadow-lg shadow-brand-900/40' : 'bg-ink-900/60 border border-ink-700 hover:border-brand-500/60 text-ink-100')">
                    Choose plan →
                  </a>
                } @else {
                  <a routerLink="/register"
                     [class]="'block text-center font-medium rounded-xl px-5 py-3 transition ' + (plan.featured ? 'bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white shadow-lg shadow-brand-900/40' : 'bg-ink-900/60 border border-ink-700 hover:border-brand-500/60 text-ink-100')">
                    Start →
                  </a>
                }
              </div>
            </div>
          }
        </div>
      </section>

      <!-- ===== Upsells / Add-ons ===== -->
      <section class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 py-12 lg:py-16">
        <div class="text-center mb-10">
          <div class="text-xs uppercase tracking-wider text-fuchsia-300 mb-3">Add-ons</div>
          <h2 class="font-display text-2xl lg:text-3xl font-semibold tracking-tight">
            Need something specific?
          </h2>
          <p class="mt-3 text-ink-400 max-w-2xl mx-auto text-sm">
            One-off purchases that stack on top of any plan. No subscription required.
          </p>
        </div>

        <div class="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
          @for (addon of addons; track addon.title) {
            <div class="glass rounded-xl p-5 hover:-translate-y-1 hover:border-brand-500/40 transition flex flex-col">
              <div class="inline-flex items-center self-start text-sm font-display font-semibold px-2.5 py-1 rounded-full bg-gradient-to-r from-brand-600/20 to-fuchsia-600/20 border border-brand-500/30 text-brand-200">
                \${{ addon.price }}
              </div>
              <div class="font-display text-lg font-semibold mt-3">{{ addon.title }}</div>
              <div class="text-sm text-ink-400 mt-1 flex-1">{{ addon.copy }}</div>
              @if (signedIn()) {
                <a routerLink="/dashboard"
                   class="mt-4 block text-center text-sm font-medium rounded-lg px-4 py-2 bg-ink-900/60 border border-ink-700 hover:border-brand-500/60 text-ink-100 transition">
                  Buy as add-on →
                </a>
              } @else {
                <a routerLink="/register"
                   class="mt-4 block text-center text-sm font-medium rounded-lg px-4 py-2 bg-ink-900/60 border border-ink-700 hover:border-brand-500/60 text-ink-100 transition">
                  Buy as add-on →
                </a>
              }
            </div>
          }
        </div>
      </section>

      <div class="max-w-7xl mx-auto px-6 lg:px-10"><app-payfast-subscribe productName="MADAuthor" headline="Publish with Payfast" lead="Choose the writing plan, confirm your email, and subscribe in a secure onsite Payfast window." [compact]="true"></app-payfast-subscribe></div>

      <!-- ===== Final CTA ===== -->
      <section class="relative z-10 max-w-5xl mx-auto px-6 lg:px-10 py-20 lg:py-28">
        <div class="glass rounded-3xl p-10 lg:p-16 text-center relative overflow-hidden">
          <div class="absolute inset-0 bg-gradient-to-br from-brand-600/10 via-fuchsia-600/10 to-transparent pointer-events-none"></div>
          <div class="relative">
            <h2 class="font-display text-4xl lg:text-6xl font-semibold tracking-tight leading-tight">
              The blank page
              <span class="bg-gradient-to-r from-brand-300 to-fuchsia-300 bg-clip-text text-transparent">just lost.</span>
            </h2>
            <p class="mt-5 text-lg text-ink-300 max-w-2xl mx-auto">
              Pitch the idea you've been carrying around. Walk away. Come back to a finished book.
            </p>
            <div class="mt-10">
              @if (signedIn()) {
                <a routerLink="/books/new"
                   class="inline-flex items-center gap-2 bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-xl px-8 py-4 text-lg transition shadow-2xl shadow-brand-900/50">
                  Start a new book →
                </a>
              } @else {
                <a routerLink="/register"
                   class="inline-flex items-center gap-2 bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-xl px-8 py-4 text-lg transition shadow-2xl shadow-brand-900/50">
                  Start writing my book &mdash; free →
                </a>
                <div class="mt-4 text-xs text-ink-500">No credit card · No setup · Cancel any time</div>
              }
            </div>
          </div>
        </div>
      </section>

      <!-- ===== Footer ===== -->
      <footer class="relative z-10 max-w-7xl mx-auto px-6 lg:px-10 py-10 border-t border-ink-800/70 text-xs text-ink-500 flex flex-wrap items-center justify-between gap-3">
        <div>
          © {{ year }} MADAuthor · The AI publishing studio
        </div>
        <div class="flex items-center gap-5">
          <a routerLink="/login" class="hover:text-ink-200 transition">Sign in</a>
          <a routerLink="/register" class="hover:text-ink-200 transition">Register</a>
        </div>
      </footer>
    </div>
  `,
  styles: [`
    /* Subtle headline shimmer */
    @keyframes shimmer {
      0%, 100% { background-position: 0% 50%; }
      50%      { background-position: 100% 50%; }
    }
    .mad-universe-strip {
      position: relative;
      overflow: hidden;
      background: #0d1628;
      border-top: 1px solid rgba(148, 163, 184, 0.16);
      border-bottom: 1px solid rgba(148, 163, 184, 0.16);
      padding: 16px 0;
    }
    .mad-universe-inner {
      display: flex;
      align-items: center;
      gap: 24px;
      max-width: 1180px;
      margin: 0 auto;
      padding: 0 24px;
    }
    .mad-universe-kicker {
      flex: 0 0 auto;
      display: inline-flex;
      align-items: center;
      gap: 8px;
      margin: 0;
      color: #FF4081;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.18em;
      line-height: 1;
      text-transform: uppercase;
      white-space: nowrap;
    }
    .mad-universe-kicker span { color: #D500F9; }
    .mad-universe-marquee {
      flex: 1 1 auto;
      min-width: 0;
      overflow: hidden;
      -webkit-mask-image: linear-gradient(90deg, transparent, #000 8%, #000 92%, transparent);
      mask-image: linear-gradient(90deg, transparent, #000 8%, #000 92%, transparent);
    }
    .mad-universe-track {
      display: flex;
      align-items: center;
      gap: 36px;
      width: max-content;
      animation: madUniverseScroll 44s linear infinite;
    }
    .mad-universe-link {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: max-content;
      opacity: 0.78;
      text-decoration: none;
      transition: opacity 160ms ease, transform 160ms ease;
    }
    .mad-universe-link:hover { opacity: 1; transform: translateY(-1px); }
    .mad-universe-link img {
      display: block;
      width: auto;
      max-width: 168px;
      height: 24px;
      object-fit: contain;
      filter: drop-shadow(0 0 12px rgba(255,255,255,0.08));
    }
    .mad-universe-text {
      color: #cbd5e1;
      font-size: 12px;
      font-weight: 800;
      letter-spacing: 0.1em;
      line-height: 1;
      text-transform: uppercase;
      white-space: nowrap;
    }
    @keyframes madUniverseScroll {
      from { transform: translateX(0); }
      to { transform: translateX(-50%); }
    }
    @media (max-width: 760px) {
      .mad-universe-inner {
        align-items: stretch;
        flex-direction: column;
        gap: 12px;
        padding: 0 18px;
      }
      .mad-universe-kicker { justify-content: center; }
      .mad-universe-track { gap: 28px; animation-duration: 52s; }
      .mad-universe-link img { height: 20px; max-width: 142px; }
    }
    @media (prefers-reduced-motion: reduce) {
      .mad-universe-track {
        animation: none;
        flex-wrap: wrap;
        justify-content: center;
        width: auto;
      }
      .mad-universe-marquee {
        -webkit-mask-image: none;
        mask-image: none;
      }
    }
  `],
})
export class LandingComponent {
  private auth = inject(AuthService);

  protected readonly madUniverseApps = MAD_UNIVERSE_APPS;
  signedIn = computed(() => this.auth.isAuthenticated());

  year = new Date().getFullYear();

  pipeline = [
    { icon: '🧭', label: 'Planner',     copy: 'Turns an idea into a 12–20 chapter outline.' },
    { icon: '🔎', label: 'Researcher',  copy: 'Pulls facts and references where the book needs them.' },
    { icon: '✍️', label: 'Writer',      copy: 'Drafts every chapter in your chosen voice.' },
    { icon: '🪶', label: 'Editor',      copy: 'Sharpens prose, tightens flow, preserves voice.' },
    { icon: '🧵', label: 'Continuity',  copy: 'Reads the whole book at once; flags contradictions.' },
    { icon: '🎨', label: 'Cover',       copy: 'AI cover art + typography. KDP wrap and screen versions.' },
    { icon: '📰', label: 'Publisher',   copy: 'KDP description, BISAC codes, keywords, copyright page.' },
    { icon: '📣', label: 'Marketer',    copy: 'Social posts, launch emails, ad variants ready to paste.' },
  ];

  // Pricing toggle: 'monthly' | 'yearly'
  billing = signal<'monthly' | 'yearly'>('monthly');

  plans: {
    id: string;
    name: string;
    tagline: string;
    monthly: number;
    yearly: number;
    featured: boolean;
    features: { label: string; value: true | false | string }[];
  }[] = [
    {
      id: 'author',
      name: 'Author',
      tagline: 'The starter for self-published authors.',
      monthly: 19,
      yearly: 190,
      featured: false,
      features: [
        { label: 'Books per month',                                  value: '2' },
        { label: 'AI agents (Plan / Draft / Edit / Continuity)',     value: true },
        { label: 'Screen formats (PDF · EPUB · DOCX)',               value: true },
        { label: 'HTML + Markdown exports',                          value: true },
        { label: 'Print-ready PDFs (KDP + Ingram)',                  value: false },
        { label: 'AI cover generation',                              value: false },
        { label: 'Translation to any language',                      value: false },
        { label: 'MADCloud audio transcription',                     value: false },
        { label: 'OCR for image / scanned PDF uploads',              value: false },
        { label: 'Priority worker queue',                            value: false },
        { label: 'Email support',                                    value: true },
      ],
    },
    {
      id: 'pro',
      name: 'Pro',
      tagline: "The serious author's studio.",
      monthly: 49,
      yearly: 490,
      featured: true,
      features: [
        { label: 'Books per month',                                  value: '10' },
        { label: 'AI agents (Plan / Draft / Edit / Continuity)',     value: true },
        { label: 'Screen formats (PDF · EPUB · DOCX)',               value: true },
        { label: 'HTML + Markdown exports',                          value: true },
        { label: 'Print-ready PDFs (KDP + Ingram, 6×9, mirrored)',   value: true },
        { label: 'MADCloud cover generation',                        value: true },
        { label: 'Translation to any language',                      value: true },
        { label: 'MADCloud audio transcription',                     value: true },
        { label: 'OCR for image / scanned PDF uploads',              value: true },
        { label: 'Priority worker queue',                            value: true },
        { label: 'Email support',                                    value: true },
      ],
    },
    {
      id: 'studio',
      name: 'Studio',
      tagline: 'Teams + agencies.',
      monthly: 149,
      yearly: 1490,
      featured: false,
      features: [
        { label: 'Books per month',                                  value: 'Unlimited' },
        { label: 'AI agents (Plan / Draft / Edit / Continuity)',     value: true },
        { label: 'Screen formats (PDF · EPUB · DOCX)',               value: true },
        { label: 'HTML + Markdown exports',                          value: true },
        { label: 'Print-ready PDFs (KDP + Ingram, 6×9, mirrored)',   value: true },
        { label: 'MADCloud cover generation',                        value: true },
        { label: 'Translation to any language',                      value: true },
        { label: 'MADCloud audio transcription',                     value: true },
        { label: 'OCR for image / scanned PDF uploads',              value: true },
        { label: 'Priority worker queue',                            value: 'Fastest ✦' },
        { label: 'Custom agent prompts',                             value: true },
        { label: 'Team seats',                                       value: 'Up to 5' },
        { label: 'API access',                                       value: true },
        { label: 'Email support',                                    value: 'Priority' },
      ],
    },
  ];

  addons = [
    {
      price: 9,
      title: 'KDP print-ready PDF',
      copy: 'A single, KDP-spec interior PDF for one manuscript. One-off, no subscription.',
    },
    {
      price: 5,
      title: 'AI cover generation',
      copy: 'One AI-generated cover for one book, with KDP wrap and screen versions.',
    },
    {
      price: 19,
      title: 'Whole-book translation',
      copy: 'Translate an entire manuscript into one target language of your choice.',
    },
    {
      price: 29,
      title: 'Editorial pass',
      copy: 'Extra continuity sweep + tone editing pass on one finished manuscript.',
    },
  ];
}
