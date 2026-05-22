import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';

const PUBLIC_ENDPOINTS = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();

  // Rewrite relative /api/* and /hubs/* paths to the absolute API URL in production.
  // Dev keeps relative paths and lets the ng-serve proxy forward to localhost:5150.
  const isRelativeApi =
    (req.url.startsWith('/api/') || req.url.startsWith('/hubs/')) &&
    !!environment.apiBase;
  let outgoing = req;
  if (isRelativeApi) {
    outgoing = req.clone({ url: environment.apiBase + req.url, withCredentials: true });
  } else if (req.url.startsWith('/api/') || req.url.startsWith('/hubs/')) {
    outgoing = req.clone({ withCredentials: true });
  }

  const isApi = outgoing.url.includes('/api/') || outgoing.url.includes('/hubs/');
  const isPublic = PUBLIC_ENDPOINTS.some((p) => outgoing.url.endsWith(p));

  if (token && isApi && !isPublic) {
    outgoing = outgoing.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  // Plesk on Windows + IIS reject DELETE / PUT / PATCH at the server level, AND
  // reject POST to URLs ending in a {guid}, AND Plesk's WAF flags HTTP-verb
  // keywords ('delete', 'put') anywhere in the URL, AND the WAF flags the
  // `X-HTTP-Method-Override` header itself (known method-override attack vector).
  // Workaround: tunnel as POST with the verb encoded in a single-letter URL
  // suffix — `/_d` = DELETE, `/_p` = PUT, `/_h` = PATCH. No custom headers, no
  // keyword-matching segments, no guid as the tail. The API middleware reads
  // the suffix, strips it, and swaps the method back before routing.
  if (isApi && (outgoing.method === 'DELETE' || outgoing.method === 'PUT' || outgoing.method === 'PATCH')) {
    const suffix =
      outgoing.method === 'DELETE' ? '/_d' :
      outgoing.method === 'PUT'    ? '/_p' :
      /* PATCH */                    '/_h';
    const [pathPart, queryPart] = outgoing.url.split('?', 2);
    const rewritten = pathPart.replace(/\/+$/, '') + suffix + (queryPart ? '?' + queryPart : '');
    outgoing = outgoing.clone({ url: rewritten, method: 'POST' });
  }

  return next(outgoing).pipe(
    catchError((err) => {
      // On a 401 to a protected endpoint, try a single refresh + replay.
      if (err?.status === 401 && isApi && !isPublic) {
        return auth.refresh().pipe(
          switchMap(() => {
            const fresh = auth.accessToken();
            // Replay against `outgoing` (which already has the rewritten absolute URL),
            // not `req` (the original relative URL). Otherwise the replay hits the SPA
            // host instead of the API host and 404s.
            const replay = outgoing.clone({
              withCredentials: true,
              setHeaders: fresh ? { Authorization: `Bearer ${fresh}` } : {},
            });
            return next(replay);
          }),
          catchError((refreshErr) => throwError(() => refreshErr)),
        );
      }
      return throwError(() => err);
    }),
  );
};
