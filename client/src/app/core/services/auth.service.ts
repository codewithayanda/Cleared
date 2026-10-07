import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, firstValueFrom, tap, timeout } from 'rxjs';
import { environment } from '@env';
import { authEndpoint } from '@core/interceptors/auth-context';
import { AuthResponse, LoginRequest, RegisterRequest } from '@core/models/auth.model';
import { signedInHint } from '@core/utils/signed-in-hint';
import { withTabLock } from '@core/utils/tab-lock';

interface DecodedClaims {
  tenant_id?: string;
  iat?: number;
  exp?: number;
}

const REFRESH_LOCK = 'cleared-refresh';
const REFRESH_TIMEOUT_MS = 10_000;
const EXPIRY_MARGIN_MS = 30_000;
const CHANNEL_NAME = 'cleared-auth';
const SIGNED_OUT = 'signed-out';

// The access token lives in memory only, never localStorage or sessionStorage: both are readable
// by any script, so one XSS would leak a live session. The refresh token is an HttpOnly cookie
// the page cannot read, and a reload trades it for a new access token.
@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL_NAME);

  private readonly accessToken = signal<string | null>(null);
  private readonly tenantId = signal<string | null>(null);
  private expiresAt = Number.POSITIVE_INFINITY;
  private refreshing: Promise<string | null> | null = null;
  private epoch = 0;

  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  constructor() {
    this.channel?.addEventListener('message', (event: MessageEvent) => {
      if (event.data === SIGNED_OUT) {
        this.clearSession();
        this.router.navigateByUrl('/login');
      }
    });
  }

  ngOnDestroy(): void {
    this.channel?.close();
  }

  currentAccessToken(): string | null {
    return this.accessToken();
  }

  // True when the token has under 30 seconds left, so a call should wait for a fresh one.
  isExpiring(): boolean {
    return this.accessToken() !== null && this.expiresAt - Date.now() < EXPIRY_MARGIN_MS;
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiUrl}/auth/register`, request, {
        context: authEndpoint(),
      })
      .pipe(tap((response) => this.setSession(response)));
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiUrl}/auth/login`, request, {
        context: authEndpoint(),
      })
      .pipe(tap((response) => this.setSession(response)));
  }

  // Runs once, before the first route. Without the flag nobody can be signed in, so skip the call.
  async restore(): Promise<void> {
    if (!signedInHint.isSet()) {
      return;
    }

    try {
      await this.refreshSession();
    } catch {
      // The API could not be reached, so start signed out. The flag stays for the next load.
    }
  }

  // Callers in this tab share one request, and the lock makes tabs take turns: each tab reads the
  // shared cookie only when it sends, so the second one sends the cookie the first one just got.
  // Resolves null when the session is over, and rejects when the refresh could not be done.
  refreshSession(): Promise<string | null> {
    this.refreshing ??= withTabLock(REFRESH_LOCK, () => this.exchange()).finally(() => {
      this.refreshing = null;
    });

    return this.refreshing;
  }

  // The server ends the session first, so a copy of the cookie cannot bring it back.
  logout(): Observable<void> {
    return this.http
      .post<void>(`${environment.apiUrl}/auth/logout`, null, { context: authEndpoint() })
      .pipe(
        tap(() => {
          this.clearSession();
          this.channel?.postMessage(SIGNED_OUT);
          this.router.navigateByUrl('/login');
        }),
      );
  }

  clearSession(): void {
    this.epoch++;
    this.accessToken.set(null);
    this.tenantId.set(null);
    this.expiresAt = Number.POSITIVE_INFINITY;
    signedInHint.clear();
  }

  private async exchange(): Promise<string | null> {
    const epoch = this.epoch;

    try {
      const response = await firstValueFrom(
        this.http
          .post<AuthResponse>(`${environment.apiUrl}/auth/refresh`, null, {
            context: authEndpoint(),
          })
          .pipe(timeout(REFRESH_TIMEOUT_MS)),
      );

      // Signed out while the request was on its way, so the answer is no use any more.
      if (epoch !== this.epoch) {
        return null;
      }

      this.setSession(response);

      return response.accessToken;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.clearSession();

        return null;
      }

      throw error;
    }
  }

  private setSession(response: AuthResponse): void {
    const claims = decodeClaims(response.accessToken);

    this.accessToken.set(response.accessToken);
    this.tenantId.set(claims?.tenant_id ?? null);
    this.expiresAt = expiryFrom(claims);
    signedInHint.set();
  }
}

// Decoded for display and timing only. Authorization is never decided client-side; the server
// checks the signed claims on every request.
function decodeClaims(token: string): DecodedClaims | null {
  try {
    const payload = token.split('.')[1];
    const decoded = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));

    return JSON.parse(decoded) as DecodedClaims;
  } catch {
    return null;
  }
}

// Counted from the moment the token arrives, using the token's own lifetime, so a wrong clock on
// this machine cannot make every call look expired and start a refresh each time.
function expiryFrom(claims: DecodedClaims | null): number {
  return claims?.exp !== undefined && claims.iat !== undefined
    ? Date.now() + (claims.exp - claims.iat) * 1000
    : Number.POSITIVE_INFINITY;
}
