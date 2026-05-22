import { Routes } from '@angular/router';
import { authGuard, anonGuard, adminGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  // Unauthenticated routes — flat so the router doesn't have to resolve nested '' paths.
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

  // Authenticated app — single root with the shell component and lazy children.
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
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
        ],
      },
      {
        path: 'admin/queue',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/admin-queue.component').then((m) => m.AdminQueueComponent),
      },
      {
        path: 'admin/claude',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/claude/claude.page').then((m) => m.ClaudePageComponent),
      },
    ],
  },

  // Catch-all goes to the dashboard (authGuard bounces unauthenticated visitors to /login).
  { path: '**', redirectTo: '' },
];
