import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import { Payment, RecordPaymentRequest } from '@core/models/payment.model';

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/invoices`;

  record(invoiceId: string, request: RecordPaymentRequest): Observable<Payment> {
    return this.http.post<Payment>(`${this.baseUrl}/${invoiceId}/payments`, request);
  }
}
