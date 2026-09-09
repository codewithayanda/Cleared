import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import { AuditLogEntry } from '@core/models/audit-log.model';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/audit`;

  list(): Observable<AuditLogEntry[]> {
    return this.http.get<AuditLogEntry[]>(this.baseUrl);
  }
}
