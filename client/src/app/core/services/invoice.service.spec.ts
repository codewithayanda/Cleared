import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '@env';
import { UpdateInvoiceRequest } from '@core/models/invoice.model';
import { IDEMPOTENCY_HEADER } from '@core/utils/idempotency-key';
import { InvoiceService } from './invoice.service';

const URL = `${environment.apiUrl}/invoices`;
const WHOLE_DRAFT: UpdateInvoiceRequest = {
  customerId: 'c1',
  lines: [{ description: 'Design', quantity: 2, unitPrice: '50.00', vatTreatment: 'Standard' }],
};

describe('InvoiceService', () => {
  let service: InvoiceService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(InvoiceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('changes a draft with a PUT that carries the whole draft and needs no Idempotency-Key', () => {
    service.update('i1', WHOLE_DRAFT).subscribe();

    const request = httpMock.expectOne(`${URL}/i1`);

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(WHOLE_DRAFT);
    expect(request.request.headers.has(IDEMPOTENCY_HEADER)).toBe(false);
    request.flush({});
  });

  it('deletes a draft with a DELETE that needs no Idempotency-Key', () => {
    service.delete('i1').subscribe();

    const request = httpMock.expectOne(`${URL}/i1`);

    expect(request.request.method).toBe('DELETE');
    expect(request.request.headers.has(IDEMPOTENCY_HEADER)).toBe(false);
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('still starts a draft with a POST that carries its Idempotency-Key', () => {
    service.create(WHOLE_DRAFT, 'key-1').subscribe();

    const request = httpMock.expectOne(URL);

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get(IDEMPOTENCY_HEADER)).toBe('key-1');
    request.flush({});
  });

  it('still issues with a POST that carries its Idempotency-Key', () => {
    service.issue('i1', { issueDate: '2026-11-05', dueDate: '2026-12-05' }, 'key-2').subscribe();

    const request = httpMock.expectOne(`${URL}/i1/issue`);

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get(IDEMPOTENCY_HEADER)).toBe('key-2');
    request.flush({});
  });
});
