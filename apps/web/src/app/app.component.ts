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
    // Try to silently restore the session from the refresh cookie. If it fails,
    // the route guards send the user to /login.
    this.auth.tryRestore().subscribe();
  }
}
