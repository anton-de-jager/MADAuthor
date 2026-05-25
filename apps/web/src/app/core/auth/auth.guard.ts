import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { map, of, catchError } from 'rxjs';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;

  // Not authenticated yet - try silent refresh once before bouncing to /login.
  return auth.refresh().pipe(
    map(() => true),
    catchError(() => {
      router.navigate(['/login']);
      return of(false);
    }),
  );
};

export const anonGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) {
    router.navigate(['/dashboard']);
    return false;
  }
  return true;
};

/**
 * Restricts a route to users with the "Admin" or "Owner" role. Used by the operator
 * /admin/claude page. Falls back to silent refresh once (same pattern as authGuard)
 * because we may be racing the page boot.
 */
export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAdmin = () => {
    const roles = auth.user()?.roles ?? [];
    return roles.includes('Admin') || roles.includes('Owner');
  };

  if (auth.isAuthenticated()) {
    if (isAdmin()) return true;
    router.navigate(['/dashboard']);
    return false;
  }

  return auth.refresh().pipe(
    map(() => {
      if (isAdmin()) return true;
      router.navigate(['/dashboard']);
      return false;
    }),
    catchError(() => {
      router.navigate(['/login']);
      return of(false);
    }),
  );
};
