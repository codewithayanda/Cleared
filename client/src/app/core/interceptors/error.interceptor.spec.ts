import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let clearSessionSpy: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(() => {
    clearSessionSpy = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { clearSession: clearSessionSpy } },
      ],
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl');
  });

  afterEach(() => httpMock.verify());

  it('clears the session and redirects to /login on a 401', () => {
    httpClient.get('/api/v1/invoices').subscribe({ error: () => undefined });

    httpMock
      .expectOne('/api/v1/invoices')
      .flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

    expect(clearSessionSpy).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('leaves the session alone on other errors', () => {
    httpClient.get('/api/v1/invoices').subscribe({ error: () => undefined });

    httpMock
      .expectOne('/api/v1/invoices')
      .flush('Server error', { status: 500, statusText: 'Server Error' });

    expect(clearSessionSpy).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
