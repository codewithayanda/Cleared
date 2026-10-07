import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '@core/services/auth.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
})
export class Shell {
  protected readonly auth = inject(AuthService);

  protected readonly signingOut = signal(false);
  protected readonly signOutFailed = signal(false);

  protected logout(): void {
    this.signingOut.set(true);
    this.signOutFailed.set(false);

    this.auth.logout().subscribe({
      error: () => {
        this.signingOut.set(false);
        this.signOutFailed.set(true);
      },
    });
  }
}
