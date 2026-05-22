import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, tap, catchError } from 'rxjs';

export interface UserSummary {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  avatarUrl?: string | null;
  companyId: string;
  /** Roles assigned to this user (e.g. "Admin", "Owner", "User", "Author"). */
  roles: string[];
}

interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: UserSummary;
}

/**
 * Register no longer auto-signs the user in — the API requires the user to confirm their email
 * first, then sign in. The response just confirms an email is on the way.
 */
export interface RegisterResponse {
  needsEmailConfirmation: boolean;
  email: string;
}

export interface ConfirmEmailResponse {
  confirmed: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  // Access token lives in memory only (not localStorage) — refresh via httpOnly cookie.
  private _accessToken = signal<string | null>(null);
  private _user = signal<UserSummary | null>(null);

  readonly accessToken = this._accessToken.asReadonly();
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._accessToken() !== null);

  register(payload: {
    email: string;
    password: string;
    firstName: string;
    lastName: string;
    companyName?: string;
  }): Observable<RegisterResponse> {
    // After Register the API sends a confirmation email and does NOT issue a JWT —
    // the user has to confirm before they can sign in.
    return this.http.post<RegisterResponse>('/api/auth/register', payload, {
      withCredentials: true,
    });
  }

  confirmEmail(uid: string, token: string): Observable<ConfirmEmailResponse> {
    return this.http.post<ConfirmEmailResponse>(
      '/api/auth/confirm-email',
      { userId: uid, token },
      { withCredentials: true },
    );
  }

  resendConfirmation(email: string): Observable<{ sent: boolean }> {
    return this.http.post<{ sent: boolean }>(
      '/api/auth/resend-confirmation',
      { email },
      { withCredentials: true },
    );
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { email, password }, { withCredentials: true })
      .pipe(tap((res) => this.applyAuth(res)));
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}, { withCredentials: true }).pipe(
      tap(() => this.clear()),
      catchError(() => {
        this.clear();
        return of(void 0);
      }),
    );
  }

  refresh(): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/refresh', {}, { withCredentials: true })
      .pipe(tap((res) => this.applyAuth(res)));
  }

  /** Called on app boot: silent session restore via refresh cookie. */
  tryRestore(): Observable<AuthResponse | null> {
    return this.refresh().pipe(catchError(() => of(null)));
  }

  private applyAuth(res: AuthResponse) {
    this._accessToken.set(res.accessToken);
    this._user.set(res.user);
  }

  private clear() {
    this._accessToken.set(null);
    this._user.set(null);
  }
}
