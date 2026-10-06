import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '@env';
import { CustomerService } from './customer.service';
import { InvoiceService } from './invoice.service';
import { PaymentService } from './payment.service';

const KEY = '3f2b8c1e-5a4d-4e0b-9d36-7c1a2b4d6e80';

describe('requests that create or move money', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function expectKeyedPost(url: string): void {
    const request = httpMock.expectOne(url);

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe(KEY);
    request.flush({});
  }

  it('sends the key when creating a customer', () => {
    TestBed.inject(CustomerService)
      .create({ name: 'Acme', email: null, vatNumber: null, address: null }, KEY)
      .subscribe();

    expectKeyedPost(`${environment.apiUrl}/customers`);
  });

  it('sends the key when creating an invoice', () => {
    TestBed.inject(InvoiceService).create({ customerId: 'c1', lines: [] }, KEY).subscribe();

    expectKeyedPost(`${environment.apiUrl}/invoices`);
  });

  it('sends the key when issuing an invoice', () => {
    TestBed.inject(InvoiceService)
      .issue('i1', { issueDate: '2026-10-06', dueDate: '2026-11-05' }, KEY)
      .subscribe();

    expectKeyedPost(`${environment.apiUrl}/invoices/i1/issue`);
  });

  it('sends the key when recording a payment', () => {
    TestBed.inject(PaymentService)
      .record('i1', { amount: '100.00', receivedAt: '2026-10-06', reference: null }, KEY)
      .subscribe();

    expectKeyedPost(`${environment.apiUrl}/invoices/i1/payments`);
  });
});
