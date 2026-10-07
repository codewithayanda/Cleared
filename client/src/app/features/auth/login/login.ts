import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@core/services/auth.service';
import { LOCKOUT_ATTEMPTS, LOCKOUT_MINUTES, LockoutTracker } from '@core/services/lockout-tracker';
import { rateLimitMessage } from '@core/utils/rate-limit-message';

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
  private readonly tracker = inject(LockoutTracker);
  private ticker: ReturnType<typeof setInterval> | null = null;

  protected readonly lockoutAttempts = LOCKOUT_ATTEMPTS;
  protected readonly lockoutMinutes = LOCKOUT_MINUTES;

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  private readonly lockedUntil = signal<number | null>(null);
  private readonly now = signal(Date.now());
  private readonly secondsLeft = computed(() => {
    const until = this.lockedUntil();

    return until === null ? 0 : Math.max(0, Math.ceil((until - this.now()) / 1000));
  });

  protected readonly locked = computed(() => this.secondsLeft() > 0);
  protected readonly countdown = computed(() => {
    const seconds = this.secondsLeft();

    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  constructor() {
    this.form.controls.email.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.checkLock());
    inject(DestroyRef).onDestroy(() => this.stopTicking());
  }

  protected togglePassword(): void {
    this.showPassword.update((shown) => !shown);
  }

  protected submit(): void {
    if (this.locked()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const credentials = this.form.getRawValue();

    this.auth.login(credentials).subscribe({
      next: () => {
        this.tracker.recordSuccess(credentials.email);
        this.router.navigateByUrl('/dashboard');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(this.failureMessage(error, credentials.email));
      },
    });
  }

  // The lock notice is shown by the countdown, so a failure that tips the account over has no
  // message of its own.
  private failureMessage(error: unknown, email: string): string | null {
    const limited = rateLimitMessage(error);

    if (limited) {
      return limited;
    }

    if (!(error instanceof HttpErrorResponse && error.status === 401)) {
      return 'Something went wrong. Please try again.';
    }

    const lockedUntil = this.tracker.recordFailure(email);
    this.checkLock();

    return lockedUntil === null ? 'Invalid email or password.' : null;
  }

  private checkLock(): void {
    this.stopTicking();
    this.now.set(Date.now());
    this.lockedUntil.set(this.tracker.lockedUntil(this.form.controls.email.value));

    if (this.lockedUntil() !== null) {
      this.ticker = setInterval(() => this.tick(), 1000);
    }
  }

  private tick(): void {
    this.now.set(Date.now());

    if (this.secondsLeft() === 0) {
      this.checkLock();
    }
  }

  private stopTicking(): void {
    if (this.ticker !== null) {
      clearInterval(this.ticker);
      this.ticker = null;
    }
  }
}
