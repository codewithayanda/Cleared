import { Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '@env';
import { AuthService } from '@core/services/auth.service';
import { lastActivity } from '@core/utils/last-activity';

export interface IdleTimings {
  warnAfterMs: number;
  signOutAfterMs: number;
  heartbeatMs: number;
}

const MINUTE_MS = 60_000;
const TICK_MS = 1000;
const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'scroll'];

export const IDLE_TIMINGS = new InjectionToken<IdleTimings>('IDLE_TIMINGS', {
  providedIn: 'root',
  factory: () => ({
    warnAfterMs: environment.idle.warnAfterMinutes * MINUTE_MS,
    signOutAfterMs: environment.idle.signOutAfterMinutes * MINUTE_MS,
    heartbeatMs: environment.idle.heartbeatMinutes * MINUTE_MS,
  }),
});

// Watches for someone who has gone quiet in every tab of the app. It goes by recorded times and not
// by counting ticks, so a sleeping laptop or a throttled background tab cannot make it wrong.
@Injectable({ providedIn: 'root' })
export class IdleService {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly timings = inject(IDLE_TIMINGS);

  private ticker: ReturnType<typeof setInterval> | null = null;
  private lastSeen = 0;
  private lastRenewal = 0;
  private readonly msLeft = signal<number | null>(null);

  readonly warning = computed(() => this.msLeft() !== null);
  readonly secondsLeft = computed(() => Math.ceil((this.msLeft() ?? 0) / 1000));

  // Once the warning is up, only its buttons answer it, so a nudge of the mouse cannot dismiss it.
  private readonly onActivity = (): void => {
    if (this.warning()) {
      return;
    }

    const now = Date.now();

    if (now - this.lastSeen >= TICK_MS) {
      this.record(now);
      this.renewIfDue(now);
    }
  };

  private readonly onVisibility = (): void => this.check();

  // Opening the app counts as activity, and the session was renewed a moment ago by signing in.
  start(): void {
    if (this.ticker !== null) {
      return;
    }

    const now = Date.now();
    this.record(now);
    this.lastRenewal = now;

    for (const name of ACTIVITY_EVENTS) {
      document.addEventListener(name, this.onActivity, { capture: true, passive: true });
    }

    document.addEventListener('visibilitychange', this.onVisibility);
    this.ticker = setInterval(() => this.check(), TICK_MS);
  }

  stop(): void {
    if (this.ticker === null) {
      return;
    }

    clearInterval(this.ticker);
    this.ticker = null;

    for (const name of ACTIVITY_EVENTS) {
      document.removeEventListener(name, this.onActivity, { capture: true });
    }

    document.removeEventListener('visibilitychange', this.onVisibility);
    this.msLeft.set(null);
  }

  staySignedIn(): void {
    const now = Date.now();
    this.record(now);
    this.msLeft.set(null);
    this.renew(now);
  }

  signOutNow(): void {
    this.stop();
    this.auth.signOutQuietly();
  }

  private check(): void {
    const idle = Date.now() - Math.max(this.lastSeen, lastActivity.read() ?? 0);

    if (idle >= this.timings.signOutAfterMs) {
      this.stop();
      this.auth.signOutQuietly('idle');

      return;
    }

    this.msLeft.set(idle >= this.timings.warnAfterMs ? this.timings.signOutAfterMs - idle : null);
  }

  private record(at: number): void {
    this.lastSeen = at;
    lastActivity.write(at);
  }

  // Keeps the server's idle clock moving only for someone who is really there.
  private renewIfDue(now: number): void {
    if (now - this.lastRenewal >= this.timings.heartbeatMs) {
      this.renew(now);
    }
  }

  private renew(now: number): void {
    this.lastRenewal = now;

    this.auth.refreshSession().then(
      (token) => {
        if (token === null) {
          this.router.navigateByUrl('/login');
        }
      },
      () => undefined,
    );
  }
}
