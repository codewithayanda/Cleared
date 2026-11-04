import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AccountService } from '@core/services/account.service';
import { rateLimitMessage } from '@core/utils/rate-limit-message';

@Component({
  selector: 'app-verify-email-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './verify-email-banner.html',
})
export class VerifyEmailBanner {
  protected readonly accounts = inject(AccountService);

  protected readonly sending = signal(false);
  protected readonly sent = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    // If the account cannot be read there is nothing to say yet, and the next page load asks again.
    this.accounts.load().subscribe({ error: () => undefined });
  }

  protected resend(): void {
    this.sending.set(true);
    this.sent.set(false);
    this.errorMessage.set(null);

    this.accounts.resendConfirmation().subscribe({
      next: (result) => {
        this.sending.set(false);
        this.sent.set(result === 'sent');
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.errorMessage.set(
          rateLimitMessage(error) ?? 'Could not send the link. Please try again.',
        );
      },
    });
  }
}
