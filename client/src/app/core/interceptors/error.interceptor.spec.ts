import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '@core/services/auth.service';
import { authEndpoint } from './auth-context';
import { errorInterceptor } from './error.interceptor';

const URL = '/api/v1/invoices';
const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };

describe('errorInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let router: Router;
  let token: string | null;
  let refreshSession: ReturnType<typeof vi.fn>;
  let clearSession: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    token = 'old';
    refreshSession = vi.fn(() => {
      token = 'new';

      return Promise.resolve('new');
    });
    clearSession = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { currentAccessToken: () => token, refreshSession, clearSession },
        },
      ],
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => httpMock.verify());

  const get = (headers: Record<string, string> = { Authorization: 'Bearer old' }) =>
    firstValueFrom(httpClient.get(URL, { headers }));

  it('refreshes the token and sends the call again once, with the new token', async () => {
    const call = get();
    httpMock.expectOne(URL).flush('', UNAUTHORIZED);

    const retry = await vi.waitFor(() => httpMock.expectOne(URL));
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush({ ok: true });

    expect(await call).toEqual({ ok: true });
    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(clearSession).not.toHaveBeenCalled();
  });

  it('keeps the Idempotency-Key on the call it sends again', async () => {
    const call = get({ Authorization: 'Bearer old', 'Idempotency-Key': 'key-1' });
    httpMock.expectOne(URL).flush('', UNAUTHORIZED);

    const retry = await vi.waitFor(() => httpMock.expectOne(URL));
    expect(retry.request.headers.get('Idempotency-Key')).toBe('key-1');
    retry.flush({});
    await call;
  });

  it('does not refresh again when another call has already swapped the token', async () => {
    const call = get();
    token = 'newer';
    httpMock.expectOne(URL).flush('', UNAUTHORIZED);

    const retry = await vi.waitFor(() => httpMock.expectOne(URL));
    expect(retry.request.headers.get('Authorization')).toBe('Bearer newer');
    retry.flush({});
    await call;

    expect(refreshSession).not.toHaveBeenCalled();
  });

  it('sends the user to login when the refresh finds the session over', async () => {
    refreshSession.mockResolvedValue(null);
    const call = get();
    const outcome = expect(call).rejects.toMatchObject({ status: 401 });

    httpMock.expectOne(URL).flush('', UNAUTHORIZED);
    await outcome;

    expect(clearSession).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('ends the session when the call sent again is refused too, with no second refresh', async () => {
    const call = get();
    const outcome = expect(call).rejects.toMatchObject({ status: 401 });
    httpMock.expectOne(URL).flush('', UNAUTHORIZED);

    const retry = await vi.waitFor(() => httpMock.expectOne(URL));
    retry.flush('', UNAUTHORIZED);
    await outcome;

    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(clearSession).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('hands back the original 401 and keeps the session when the refresh could not be done', async () => {
    refreshSession.mockRejectedValue(new Error('offline'));
    const call = get();
    const outcome = expect(call).rejects.toMatchObject({ status: 401 });

    httpMock.expectOne(URL).flush('', UNAUTHORIZED);
    await outcome;

    expect(clearSession).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('treats a 401 from an auth endpoint as an answer, not as an expired token', async () => {
    const call = firstValueFrom(
      httpClient.post('/api/v1/auth/login', {}, { context: authEndpoint() }),
    );
    const outcome = expect(call).rejects.toMatchObject({ status: 401 });

    httpMock.expectOne('/api/v1/auth/login').flush('', UNAUTHORIZED);
    await outcome;

    expect(refreshSession).not.toHaveBeenCalled();
    expect(clearSession).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('leaves the session alone on other errors', async () => {
    const call = get();
    const outcome = expect(call).rejects.toMatchObject({ status: 500 });

    httpMock.expectOne(URL).flush('Server error', { status: 500, statusText: 'Server Error' });
    await outcome;

    expect(refreshSession).not.toHaveBeenCalled();
    expect(clearSession).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
