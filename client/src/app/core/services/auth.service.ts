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
} from '@core/models/auth.model';
import { TenantResponse } from '@core/models/tenant.model';

interface DecodedClaims {
  sub: string;
  tenant_id: string;
  exp: number;
}

// In-memory only, never localStorage or sessionStorage: both are readable by any script,
// so one XSS would leak a live session. Cost: a page reload logs the user out.
// TODO: revisit once the API has a refresh-token flow.
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

  // Decoded for display only. Authorization is never decided client-side; the server checks
  // the signed claim on every request.
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
