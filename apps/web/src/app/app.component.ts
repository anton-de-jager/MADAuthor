import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
export class AppComponent implements OnInit {
  private auth = inject(AuthService);

  ngOnInit() {
    const publicPaths = new Set(['/', '/home', '/login', '/register', '/confirm-email']);
    if (publicPaths.has(window.location.pathname)) {
      return;
    }

    // Try to silently restore the session from the refresh cookie for protected
    // app routes. Public pages avoid an expected anonymous 401 in the console.
    this.auth.tryRestore().subscribe();
  }
}
