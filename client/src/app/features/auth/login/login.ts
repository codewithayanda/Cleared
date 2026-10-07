import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@core/services/auth.service';
import { rateLimitMessage } from '@core/utils/rate-limit-message';

// Mirrors the lockout in the API's Program.cs: 5 wrong passwords lock an account for 15 minutes.
const LOCKOUT_ATTEMPTS = 5;
const LOCKOUT_MINUTES = 15;

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // The page counts its own wrong passwords, per email. The server never says an account is locked,
  // because that would tell anyone which emails have accounts, so this is all the page can know.
  private readonly wrongPasswords = new Map<string, number>();

  protected readonly lockoutAttempts = LOCKOUT_ATTEMPTS;
  protected readonly lockoutMinutes = LOCKOUT_MINUTES;

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  protected togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const credentials = this.form.getRawValue();

    this.auth.login(credentials).subscribe({
      next: () => this.router.navigateByUrl('/dashboard'),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(
          rateLimitMessage(error) ?? this.failureMessage(error, credentials.email),
        );
      },
    });
  }

  private failureMessage(error: unknown, email: string): string {
    if (!(error instanceof HttpErrorResponse && error.status === 401)) {
      return 'Something went wrong. Please try again.';
    }

    const key = email.trim().toLowerCase();
    const count = (this.wrongPasswords.get(key) ?? 0) + 1;
    this.wrongPasswords.set(key, count);

    return count >= LOCKOUT_ATTEMPTS
      ? `Too many wrong passwords. If this account exists, it is locked for ${LOCKOUT_MINUTES} minutes. Wait, then try again.`
      : 'Invalid email or password.';
  }
}
