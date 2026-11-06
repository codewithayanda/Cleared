import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import {
  CreateInvoiceRequest,
  Invoice,
  IssueInvoiceRequest,
  UpdateInvoiceRequest,
} from '@core/models/invoice.model';
import { IDEMPOTENCY_HEADER } from '@core/utils/idempotency-key';

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/invoices`;

  list(): Observable<Invoice[]> {
    return this.http.get<Invoice[]>(this.baseUrl);
  }

  getById(id: string): Observable<Invoice> {
    return this.http.get<Invoice>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateInvoiceRequest, idempotencyKey: string): Observable<Invoice> {
    return this.http.post<Invoice>(this.baseUrl, request, {
      headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
    });
  }

  // A PUT, so doing it twice has the effect of doing it once and it needs no Idempotency-Key.
  update(id: string, request: UpdateInvoiceRequest): Observable<Invoice> {
    return this.http.put<Invoice>(`${this.baseUrl}/${id}`, request);
  }

  // The second of two deletes finds the draft gone and gets a 404, so it needs no Idempotency-Key.
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  issue(id: string, request: IssueInvoiceRequest, idempotencyKey: string): Observable<Invoice> {
    return this.http.post<Invoice>(`${this.baseUrl}/${id}/issue`, request, {
      headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
    });
  }

  // A blob, not JSON: the one endpoint on this service that isn't. The caller must revoke
  // whatever object URL it creates from this.
  downloadPdf(id: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${id}/pdf`, { responseType: 'blob' });
  }
}
