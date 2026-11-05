import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { IdleService } from '@core/services/idle.service';
import { IdleWarning } from '../idle-warning/idle-warning';
import { VerifyEmailBanner } from '../verify-email-banner/verify-email-banner';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, VerifyEmailBanner, IdleWarning],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
})
export class Shell {
  protected readonly auth = inject(AuthService);

  protected readonly signingOut = signal(false);
  protected readonly signOutFailed = signal(false);

  // The shell only exists for someone who is signed in, so watching for inactivity lives and dies with it.
  constructor() {
    const idle = inject(IdleService);

    idle.start();
    inject(DestroyRef).onDestroy(() => idle.stop());
  }

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
