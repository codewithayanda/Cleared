import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@core/services/auth.service';
import { rateLimitMessage, retryAfterSeconds } from '@core/utils/rate-limit-message';

// Mirrors the sign-in throttle in the API: 5 wrong passwords lock an email for 15 minutes.
const LOCKOUT_TRIES = 5;
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
  private ticker: ReturnType<typeof setInterval> | null = null;

  protected readonly lockoutTries = LOCKOUT_TRIES;
  protected readonly lockoutMinutes = LOCKOUT_MINUTES;

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  private readonly waitUntil = signal<number | null>(null);
  private readonly now = signal(Date.now());
  private readonly secondsLeft = computed(() => {
    const until = this.waitUntil();

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

    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => this.router.navigateByUrl('/dashboard'),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.fail(error);
      },
    });
  }

  private fail(error: unknown): void {
    const wait = retryAfterSeconds(error);

    if (wait !== null) {
      this.waitFor(wait);
      return;
    }

    this.errorMessage.set(
      rateLimitMessage(error) ??
        (error instanceof HttpErrorResponse && error.status === 401
          ? 'Invalid email or password.'
          : 'Something went wrong. Please try again.'),
    );
  }

  // The server says how long to wait, for a locked email and a busy address alike. The page keeps
  // no count of its own, so there is nothing in the browser to reload, copy or fool.
  private waitFor(seconds: number): void {
    this.stopTicking();
    this.errorMessage.set(null);
    this.now.set(Date.now());
    this.waitUntil.set(this.now() + seconds * 1000);
    this.ticker = setInterval(() => this.tick(), 1000);
  }

  private tick(): void {
    this.now.set(Date.now());

    if (this.secondsLeft() === 0) {
      this.stopTicking();
      this.waitUntil.set(null);
    }
  }

  private stopTicking(): void {
    if (this.ticker !== null) {
      clearInterval(this.ticker);
      this.ticker = null;
    }
  }
}
