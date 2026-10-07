import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '@env';
import { authInterceptor } from '@core/interceptors/auth.interceptor';
import { errorInterceptor } from '@core/interceptors/error.interceptor';
import { tokenLivingFor } from '@core/testing/jwt';
import { AuthService } from '@core/services/auth.service';
import { appConfig } from './app.config';

const API = environment.apiUrl;
const REFRESH_URL = `${API}/auth/refresh`;
const HINT_KEY = 'cleared.signed-in';
const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };
const NAMES = ['a', 'b', 'c'];

const FIRST = tokenLivingFor(900, 'first');
const SECOND = tokenLivingFor(900, 'second');

// The real service and both interceptors together, so the claims that depend on all of them
// being wired the right way round are proved here and not only in the pieces.
describe('session flow', () => {
  let auth: AuthService;
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });

    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  function signIn(token: string): void {
    auth.login({ email: 'a@b.co.za', password: 'secret1234' }).subscribe();
    httpMock.expectOne(`${API}/auth/login`).flush({ accessToken: token });
  }

  const callAll = () => NAMES.map((name) => firstValueFrom(http.get(`${API}/${name}`)));

  async function answerRetries(token: string): Promise<void> {
    for (const name of NAMES) {
      const retry = await vi.waitFor(() => httpMock.expectOne(`${API}/${name}`));
      expect(retry.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
      retry.flush({ name });
    }
  }

  it('refreshes once for three calls that are all refused, then sends all three again', async () => {
    signIn(FIRST);
    const calls = callAll();

    for (const name of NAMES) {
      httpMock.expectOne(`${API}/${name}`).flush('', UNAUTHORIZED);
    }
    httpMock.expectOne(REFRESH_URL).flush({ accessToken: SECOND });
    await answerRetries(SECOND);

    expect(await Promise.all(calls)).toEqual([{ name: 'a' }, { name: 'b' }, { name: 'c' }]);
  });

  it('holds calls for one shared refresh when the token is about to run out', async () => {
    signIn(tokenLivingFor(20, 'short'));
    const calls = callAll();

    for (const name of NAMES) {
      httpMock.expectNone(`${API}/${name}`);
    }
    httpMock.expectOne(REFRESH_URL).flush({ accessToken: SECOND });
    await answerRetries(SECOND);

    expect(await Promise.all(calls)).toHaveLength(3);
  });

  it('goes to login when the session is over', async () => {
    signIn(FIRST);
    const call = firstValueFrom(http.get(`${API}/a`));
    const outcome = expect(call).rejects.toMatchObject({ status: 401 });

    httpMock.expectOne(`${API}/a`).flush('', UNAUTHORIZED);
    httpMock.expectOne(REFRESH_URL).flush('', UNAUTHORIZED);
    await outcome;

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('treats a wrong password as an answer, not as an expiry', async () => {
    const call = firstValueFrom(auth.login({ email: 'a@b.co.za', password: 'wrong' }));
    const outcome = expect(call).rejects.toMatchObject({ status: 401 });

    httpMock.expectOne(`${API}/auth/login`).flush('', UNAUTHORIZED);
    await outcome;

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});

describe('app startup', () => {
  afterEach(() => localStorage.clear());

  function start() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });

    return {
      status: TestBed.inject(ApplicationInitStatus),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('restores the session before the first route when someone was signed in', async () => {
    localStorage.setItem(HINT_KEY, '1');
    const { status, httpMock } = start();

    httpMock.expectOne(REFRESH_URL).flush({ accessToken: FIRST });
    await status.donePromise;

    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    httpMock.verify();
  });

  it('starts without calling the API when nobody was signed in', async () => {
    const { status, httpMock } = start();

    await status.donePromise;

    httpMock.expectNone(REFRESH_URL);
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
  });
});
