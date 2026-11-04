import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { passwordResetErrors } from '@core/utils/password-reset-errors';
import { passwordRules } from '@core/utils/password-rules';
import { rateLimitMessage } from '@core/utils/rate-limit-message';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly location = inject(Location);

  // The link carries its secret after the #, so it never reaches a server log or a Referer header.
  // It is read once, then taken out of the address bar so it does not stay in the history.
  private readonly token = inject(ActivatedRoute).snapshot.fragment ?? '';

  protected readonly submitting = signal(false);
  protected readonly changed = signal(false);
  protected readonly linkBroken = signal(this.token === '');
  protected readonly passwordErrors = signal<string[]>([]);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(10), passwordRules]],
  });

  constructor() {
    if (this.token !== '') {
      this.location.replaceState(this.location.path(false));
    }
  }

  protected get passwordNeedsWork(): boolean {
    const password = this.form.controls.password;

    return password.invalid && password.touched;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.passwordErrors.set([]);

    // Nobody is signed in by this. The new password is typed again at the sign-in page.
    this.auth
      .resetPassword({ token: this.token, newPassword: this.form.controls.password.value })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.changed.set(true);
        },
        error: (error: unknown) => {
          this.submitting.set(false);

          const found = passwordResetErrors(error);

          if (found?.link) {
            this.linkBroken.set(true);
            return;
          }

          this.passwordErrors.set(found?.password ?? []);
          this.errorMessage.set(
            rateLimitMessage(error) ?? (found ? null : 'Something went wrong. Please try again.'),
          );
        },
      });
  }
}
