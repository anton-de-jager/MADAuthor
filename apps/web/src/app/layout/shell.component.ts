import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { NotificationsService } from '../core/signalr/notifications.service';
import { ToastService } from '../core/ui/toast.service';
import { ToastsComponent } from '../core/ui/toasts.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastsComponent],
  template: `
    <!-- h-screen locks the shell to viewport height so the <main> below can own
         vertical scroll. Most pages scroll naturally inside that <main>. Pages with
         a multi-panel layout (e.g. books-read) set h-full on their root and manage
         their own internal scroll containers. -->
    <div class="h-screen overflow-hidden flex bg-ink-950 text-ink-100">
      <!-- Sidebar -->
      <aside class="hidden md:flex w-64 flex-col border-r border-ink-800/70 bg-ink-900/40 backdrop-blur">
        <div class="px-6 py-5 border-b border-ink-800/70">
          <img src="/logo-wide-MADAuthor.png" alt="MADAuthor" class="h-11 w-auto object-contain" />
          <div class="text-xs text-ink-400 mt-1">AI publishing OS</div>
        </div>
        <nav class="flex-1 px-3 py-4 space-y-1 text-sm">
          @for (item of visibleNav(); track item.path) {
            <a
              [routerLink]="item.path"
              routerLinkActive="bg-brand-600/15 text-brand-200 border-brand-500/30"
              [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
              class="flex items-center gap-3 px-3 py-2 rounded-lg border border-transparent text-ink-300 hover:text-ink-100 hover:bg-ink-800/50 transition"
            >
              <span class="w-1.5 h-1.5 rounded-full bg-current opacity-60"></span>
              {{ item.label }}
            </a>
          }
        </nav>
        <div class="px-4 py-4 border-t border-ink-800/70 text-xs text-ink-500">
          Phase&nbsp;1 scaffold
        </div>
      </aside>

      <!-- Main area -->
      <div class="flex-1 flex flex-col min-w-0">
        <header class="flex items-center justify-between px-6 py-3 border-b border-ink-800/70 bg-ink-900/30 backdrop-blur">
          <div class="text-sm text-ink-400">Welcome back, <span class="text-ink-100 font-medium">{{ firstName() }}</span></div>
          <div class="flex items-center gap-3">
            <button
              type="button"
              (click)="logout()"
              class="text-xs text-ink-300 hover:text-ink-100 px-3 py-1.5 rounded-md border border-ink-700/60 hover:border-ink-500 transition"
            >
              Sign out
            </button>
          </div>
        </header>

        <main class="flex-1 overflow-y-auto">
          <router-outlet />
        </main>
      </div>

      <!-- Global toast layer. Renders SignalR milestone events ("Sipho freshly drafted chapter 4"). -->
      <app-toasts />
    </div>
  `,
})
export class ShellComponent implements OnInit, OnDestroy {
  private auth = inject(AuthService);
  private router = inject(Router);
  private notifications = inject(NotificationsService);
  private toasts = inject(ToastService);
  private sub?: Subscription;

  firstName = computed(() => this.auth.user()?.firstName ?? 'author');

  /** Nav entries with optional `adminOnly` flag. The visible list is computed off the
   *  current user's role set so non-admins never see operator pages in the sidebar. */
  private allNav: { path: string; label: string; exact?: boolean; adminOnly?: boolean }[] = [
    { path: '/dashboard', label: 'Dashboard', exact: true },
    { path: '/books', label: 'Books' },
    { path: '/admin/queue', label: 'Worker queue', adminOnly: true },
    { path: '/admin/claude', label: 'MAD Cloud', adminOnly: true },
  ];
  visibleNav = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    const isAdmin = roles.includes('Admin') || roles.includes('Owner');
    return this.allNav.filter((item) => !item.adminOnly || isAdmin);
  });

  async ngOnInit() {
    // Open the SignalR connection up-front so milestone toasts work from any route.
    // The page-level components still call joinProject(id) to scope which book's events
    // they care about; this only opens the transport.
    try {
      await this.notifications.ensureConnected();
    } catch {
      // Silent - the user doesn't need to see infra errors. Logs in the browser console.
    }

    // Render every milestone toast SignalR pushes. milestoneToast is set server-side
    // only for Completed events, so we don't toast on every progress tick.
    this.sub = this.notifications.jobProgress$.subscribe((ev) => {
      if (ev.milestoneToast) this.toasts.push(ev.milestoneToast);
    });
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
  }

  logout() {
    this.auth.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
