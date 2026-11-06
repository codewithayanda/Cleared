import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '@env';
import { IDEMPOTENCY_HEADER } from '@core/utils/idempotency-key';
import { CustomerService } from './customer.service';

const URL = `${environment.apiUrl}/customers`;
const DETAILS = { name: 'Beta Co', vatNumber: null, email: null, address: '1 Main Road' };

describe('CustomerService', () => {
  let service: CustomerService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CustomerService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('asks for one customer by its id', () => {
    service.getById('c1').subscribe();

    const request = httpMock.expectOne(`${URL}/c1`);

    expect(request.request.method).toBe('GET');
    request.flush({});
  });

  it('changes a customer with a PUT that carries the details and needs no Idempotency-Key', () => {
    service.update('c1', DETAILS).subscribe();

    const request = httpMock.expectOne(`${URL}/c1`);

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(DETAILS);
    expect(request.request.headers.has(IDEMPOTENCY_HEADER)).toBe(false);
    request.flush({});
  });

  it('still adds a customer with a POST that carries its Idempotency-Key', () => {
    service.create(DETAILS, 'key-1').subscribe();

    const request = httpMock.expectOne(URL);

    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get(IDEMPOTENCY_HEADER)).toBe('key-1');
    request.flush({});
  });
});
