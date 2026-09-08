import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '@env';
import {
  AuthResponse,
  LoginRequest,
  RegisterTenantRequest,
  RegisterUserRequest,
  TenantResponse,
} from '@core/models/auth.model';

interface DecodedClaims {
  sub: string;
  tenant_id: string;
  exp: number;
}

// The access token is held ONLY in memory (a signal), never in localStorage or
// sessionStorage. Both are readable by any script on the page, so a single XSS bug would
// hand over a working session token. The trade-off, and it is a real one: there is no
// refresh-token/cookie flow on the backend yet (a known, separately-tracked gap), so a
// hard page reload clears this signal and the user has to log in again. That is the
// correct, honest choice given what the API actually supports today — not an oversight.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly accessToken = signal<string | null>(null);
  private readonly tenantId = signal<string | null>(null);

  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  currentAccessToken(): string | null {
    return this.accessToken();
  }

  registerTenant(request: RegisterTenantRequest): Observable<TenantResponse> {
    return this.http.post<TenantResponse>(`${environment.apiUrl}/tenants`, request);
  }

  register(request: RegisterUserRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiUrl}/auth/register`, request)
      .pipe(tap((response) => this.setSession(response)));
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiUrl}/auth/login`, request)
      .pipe(tap((response) => this.setSession(response)));
  }

  logout(): void {
    this.accessToken.set(null);
    this.tenantId.set(null);
    this.router.navigateByUrl('/login');
  }

  clearSession(): void {
    this.accessToken.set(null);
    this.tenantId.set(null);
  }

  private setSession(response: AuthResponse): void {
    this.accessToken.set(response.accessToken);
    this.tenantId.set(this.decodeTenantId(response.accessToken));
  }

  // Decoded for display purposes only (e.g. showing "which tenant am I in"). The token
  // itself is never trusted client-side for authorization — every enforcement decision
  // happens server-side against the signed claim, which is the whole point.
  private decodeTenantId(token: string): string | null {
    try {
      const payload = token.split('.')[1];
      const decoded = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
      const claims = JSON.parse(decoded) as DecodedClaims;
      return claims.tenant_id ?? null;
    } catch {
      return null;
    }
  }
}
