import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AccountService } from '@core/services/account.service';
import { AuthService } from '@core/services/auth.service';
import { isDeadLink } from '@core/utils/dead-link';
import { rateLimitMessage } from '@core/utils/rate-limit-message';

@Component({
  selector: 'app-verify-email',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './verify-email.html',
})
export class VerifyEmail {
  private readonly auth = inject(AuthService);
  private readonly accounts = inject(AccountService);
  private readonly location = inject(Location);

  // The link carries its secret after the #, so it never reaches a server log or a Referer header.
  // It is read once, then taken out of the address bar so it does not stay in the history.
  private readonly token = inject(ActivatedRoute).snapshot.fragment ?? '';

  protected readonly confirming = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly linkBroken = signal(this.token === '');
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    if (this.token !== '') {
      this.location.replaceState(this.location.path(false));
    }
  }

  // Opening the link only shows this page. The button is what confirms, so a mail scanner that
  // fetches the link cannot use it up.
  protected confirm(): void {
    this.confirming.set(true);
    this.errorMessage.set(null);

    this.auth.verifyEmail({ token: this.token }).subscribe({
      next: () => {
        this.confirming.set(false);
        this.confirmed.set(true);

        // Someone signed in on this page has a notice at the top of the app that should now go.
        if (this.auth.isAuthenticated()) {
          this.accounts.load().subscribe({ error: () => undefined });
        }
      },
      error: (error: unknown) => {
        this.confirming.set(false);

        if (isDeadLink(error)) {
          this.linkBroken.set(true);

          return;
        }

        this.errorMessage.set(rateLimitMessage(error) ?? 'Something went wrong. Please try again.');
      },
    });
  }
}
