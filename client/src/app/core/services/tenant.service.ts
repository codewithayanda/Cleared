import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import { TenantResponse, UpdateTenantProfileRequest } from '@core/models/tenant.model';

@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/tenants`;

  getMine(): Observable<TenantResponse> {
    return this.http.get<TenantResponse>(`${this.baseUrl}/me`);
  }

  updateProfile(request: UpdateTenantProfileRequest): Observable<TenantResponse> {
    return this.http.put<TenantResponse>(`${this.baseUrl}/me`, request);
  }
}
