import { Routes } from '@angular/router';
import { authGuard, anonGuard, adminGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: 'ai', canActivate: [adminGuard], loadComponent: () => import('./features/madcloud-ai/madcloud-ai.page').then((m) => m.MadcloudAiPage) },

  // Public marketing landing — full-bleed, no shell, no auth guard. Auth-aware
  // CTAs route signed-in visitors to /dashboard or /books/new; anon visitors to
  // /register and /login. pathMatch:'full' keeps this from swallowing /dashboard
  // etc. below.
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./features/landing/landing.component').then((m) => m.LandingComponent),
  },
  {
    path: 'home',
    loadComponent: () =>
      import('./features/landing/landing.component').then((m) => m.LandingComponent),
  },

  // Unauthenticated auth screens - flat so the router doesn't have to resolve nested '' paths.
  {
    path: 'login',
    canActivate: [anonGuard],
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    canActivate: [anonGuard],
    loadComponent: () => import('./features/auth/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'confirm-email',
    canActivate: [anonGuard],
    loadComponent: () =>
      import('./features/auth/confirm-email.component').then((m) => m.ConfirmEmailComponent),
  },

  // Authenticated app - single root with the shell component and lazy children.
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'books',
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/books/books-list.component').then((m) => m.BooksListComponent),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/books/books-new.component').then((m) => m.BooksNewComponent),
          },
          {
            path: ':id',
            loadComponent: () =>
              import('./features/books/books-detail.component').then((m) => m.BooksDetailComponent),
          },
          {
            path: ':id/read',
            loadComponent: () =>
              import('./features/books/books-read.component').then((m) => m.BooksReadComponent),
          },
          {
            path: ':id/publishing',
            data: { area: 'publishing' },
            loadComponent: () =>
              import('./features/books/book-workflow-page.component').then((m) => m.BookWorkflowPageComponent),
          },
          {
            path: ':id/assets',
            data: { area: 'assets' },
            loadComponent: () =>
              import('./features/books/book-workflow-page.component').then((m) => m.BookWorkflowPageComponent),
          },
          {
            path: ':id/covers',
            data: { area: 'covers' },
            loadComponent: () =>
              import('./features/books/book-workflow-page.component').then((m) => m.BookWorkflowPageComponent),
          },
          {
            path: ':id/metadata',
            data: { area: 'metadata' },
            loadComponent: () =>
              import('./features/books/book-workflow-page.component').then((m) => m.BookWorkflowPageComponent),
          },
          {
            path: ':id/exports',
            data: { area: 'exports' },
            loadComponent: () =>
              import('./features/books/book-workflow-page.component').then((m) => m.BookWorkflowPageComponent),
          },
          {
            path: ':id/translate',
            data: { area: 'translate' },
            loadComponent: () =>
              import('./features/books/book-workflow-page.component').then((m) => m.BookWorkflowPageComponent),
          },
        ],
      },
      {
        path: 'billing',
        loadComponent: () =>
          import('./features/billing/billing-page.component').then((m) => m.BillingPageComponent),
      },
      {
        path: 'billing/success',
        data: { status: 'success' },
        loadComponent: () =>
          import('./features/billing/billing-result.component').then((m) => m.BillingResultComponent),
      },
      {
        path: 'billing/cancelled',
        data: { status: 'cancelled' },
        loadComponent: () =>
          import('./features/billing/billing-result.component').then((m) => m.BillingResultComponent),
      },
      {
        path: 'admin',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/admin-home.component').then((m) => m.AdminHomeComponent),
      },
      {
        path: 'admin/queue',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/admin-queue.component').then((m) => m.AdminQueueComponent),
      },
      {
        path: 'admin/ai',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/madcloud-ai/madcloud-ai.page').then((m) => m.MadcloudAiPage),
      },
    ],
  },

  // Catch-all: authenticated users land on dashboard, unauthenticated on the landing page.
  { path: '**', redirectTo: 'dashboard' },
];
