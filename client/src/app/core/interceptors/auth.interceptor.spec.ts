import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '@core/services/auth.service';
import { authEndpoint } from './auth-context';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let token: string | null;
  let refreshSession: ReturnType<typeof vi.fn>;

  function configure(initial: string | null, expiring = false) {
    token = initial;
    refreshSession = vi.fn(() => {
      token = 'new';

      return Promise.resolve('new');
    });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            currentAccessToken: () => token,
            isExpiring: () => expiring,
            refreshSession,
          },
        },
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

  it('leaves calls to the auth endpoints alone, even when the token is about to run out', () => {
    configure('a-valid-token', true);

    httpClient.post('/api/v1/auth/logout', null, { context: authEndpoint() }).subscribe();

    const request = httpMock.expectOne('/api/v1/auth/logout');
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(refreshSession).not.toHaveBeenCalled();
    request.flush(null);
  });

  it('waits for a fresh token before sending when the current one is about to run out', async () => {
    configure('old', true);

    httpClient.get('/api/v1/customers').subscribe();
    httpMock.expectNone('/api/v1/customers');

    const request = await vi.waitFor(() => httpMock.expectOne('/api/v1/customers'));
    expect(request.request.headers.get('Authorization')).toBe('Bearer new');
    request.flush([]);
  });

  it('sends the call with the old token when the refresh cannot be done', async () => {
    configure('old', true);
    refreshSession.mockRejectedValue(new Error('offline'));

    httpClient.get('/api/v1/customers').subscribe();

    const request = await vi.waitFor(() => httpMock.expectOne('/api/v1/customers'));
    expect(request.request.headers.get('Authorization')).toBe('Bearer old');
    request.flush([]);
  });
});
