import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import { CreateCustomerRequest, Customer } from '@core/models/customer.model';
import { IDEMPOTENCY_HEADER } from '@core/utils/idempotency-key';

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/customers`;

  list(): Observable<Customer[]> {
    return this.http.get<Customer[]>(this.baseUrl);
  }

  create(request: CreateCustomerRequest, idempotencyKey: string): Observable<Customer> {
    return this.http.post<Customer>(this.baseUrl, request, {
      headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
    });
  }
}
