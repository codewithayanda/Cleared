import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@env';
import {
  CreateCustomerRequest,
  Customer,
  UpdateCustomerRequest,
} from '@core/models/customer.model';
import { IDEMPOTENCY_HEADER } from '@core/utils/idempotency-key';

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/customers`;

  list(): Observable<Customer[]> {
    return this.http.get<Customer[]>(this.baseUrl);
  }

  getById(id: string): Observable<Customer> {
    return this.http.get<Customer>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateCustomerRequest, idempotencyKey: string): Observable<Customer> {
    return this.http.post<Customer>(this.baseUrl, request, {
      headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
    });
  }

  // A PUT, so doing it twice has the effect of doing it once and it needs no Idempotency-Key.
  update(id: string, request: UpdateCustomerRequest): Observable<Customer> {
    return this.http.put<Customer>(`${this.baseUrl}/${id}`, request);
  }
}
