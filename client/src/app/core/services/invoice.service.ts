import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import { CreateInvoiceRequest, Invoice, IssueInvoiceRequest } from '@core/models/invoice.model';
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
