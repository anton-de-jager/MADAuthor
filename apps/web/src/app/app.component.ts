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
    // Silently restore the session from the refresh cookie on every page load.
    // This ensures auth-aware CTAs on public pages (landing, login) show the
    // correct state for returning users. Anonymous users produce an expected
    // 401 on the refresh call — tryRestore() swallows it.
    this.auth.tryRestore().subscribe();
  }
}
