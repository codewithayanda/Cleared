import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '@core/services/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;

  function configure(token: string | null) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentAccessToken: () => token } },
      ],
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => httpMock.verify());

  it('attaches an Authorization header when a token is present', () => {
    configure('a-valid-token');

    httpClient.get('/api/v1/customers').subscribe();

    const request = httpMock.expectOne('/api/v1/customers');
    expect(request.request.headers.get('Authorization')).toBe('Bearer a-valid-token');
    request.flush([]);
  });

  it('does not attach an Authorization header when there is no token', () => {
    configure(null);

    httpClient.get('/api/v1/customers').subscribe();

    const request = httpMock.expectOne('/api/v1/customers');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush([]);
  });
});
