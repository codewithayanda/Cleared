import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '@env';
import { RegisterRequest } from '@core/models/auth.model';
import { installFakeLocks, removeFakeLocks } from '@core/testing/fake-locks';
import { tokenLivingFor } from '@core/testing/jwt';
import { withTabLock } from '@core/utils/tab-lock';
import { AuthService } from './auth.service';

// A real (expired, harmless) HS256 token shaped like the ones the API issues, so decoding
// logic is exercised against actual JWT structure rather than a hand-typed fake string.
// Header: {"alg":"HS256","typ":"JWT"}, Payload: {"sub":"u1","tenant_id":"tenant-42","exp":1}
const SAMPLE_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJ1MSIsInRlbmFudF9pZCI6InRlbmFudC00MiIsImV4cCI6MX0' +
  '.dummy-signature';

const REGISTRATION: RegisterRequest = {
  company: { companyName: 'Acme', vatStatus: 'NotRegistered', vatNumber: null, tradingName: null },
  email: 'a@b.co.za',
  password: 'secret1234',
};

const HINT_KEY = 'cleared.signed-in';
const LOGIN_URL = `${environment.apiUrl}/auth/login`;
const REFRESH_URL = `${environment.apiUrl}/auth/refresh`;
const LOGOUT_URL = `${environment.apiUrl}/auth/logout`;
const CREDENTIALS = { email: 'a@b.co.za', password: 'secret1234' };
const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };

const FIRST = tokenLivingFor(900, 'first');
const SECOND = tokenLivingFor(900, 'second');

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    vi.useRealTimers();
  });

  function signIn(token: string): void {
    service.login(CREDENTIALS).subscribe();
    httpMock.expectOne(LOGIN_URL).flush({ accessToken: token });
  }

  it('is not authenticated before any login', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentAccessToken()).toBeNull();
  });

  it('becomes authenticated once login succeeds, storing the token in memory only', () => {
    service.login(CREDENTIALS).subscribe();

    httpMock.expectOne(LOGIN_URL).flush({ accessToken: SAMPLE_TOKEN });

    expect(service.isAuthenticated()).toBe(true);
    expect(service.currentAccessToken()).toBe(SAMPLE_TOKEN);
  });

  it('clearSession logs the user out without navigating', () => {
    signIn(SAMPLE_TOKEN);

    service.clearSession();

    expect(service.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('register also establishes a session from the returned token', () => {
    service.register(REGISTRATION).subscribe();

    httpMock.expectOne(`${environment.apiUrl}/auth/register`).flush({ accessToken: SAMPLE_TOKEN });

    expect(service.isAuthenticated()).toBe(true);
    expect(localStorage.getItem(HINT_KEY)).toBe('1');
  });

  it('register sends the company and credentials, never a tenant id', () => {
    service.register(REGISTRATION).subscribe();

    const request = httpMock.expectOne(`${environment.apiUrl}/auth/register`);

    expect(Object.keys(request.request.body as object).sort()).toEqual([
      'company',
      'email',
      'password',
    ]);
    request.flush({ accessToken: SAMPLE_TOKEN });
  });

  it('sets the signed-in flag when someone signs in and drops it when the session is cleared', () => {
    signIn(FIRST);
    expect(localStorage.getItem(HINT_KEY)).toBe('1');

    service.clearSession();

    expect(localStorage.getItem(HINT_KEY)).toBeNull();
  });

  describe('restoring a session at startup', () => {
    it('does not call the API when nobody was signed in', async () => {
      await service.restore();

      httpMock.expectNone(REFRESH_URL);
      expect(service.isAuthenticated()).toBe(false);
    });

    it('signs the person back in when the flag is set and the cookie is still good', async () => {
      localStorage.setItem(HINT_KEY, '1');

      const restored = service.restore();
      httpMock.expectOne(REFRESH_URL).flush({ accessToken: FIRST });
      await restored;

      expect(service.currentAccessToken()).toBe(FIRST);
    });

    it('starts signed out and drops the flag when the session is over', async () => {
      localStorage.setItem(HINT_KEY, '1');

      const restored = service.restore();
      httpMock.expectOne(REFRESH_URL).flush('', UNAUTHORIZED);
      await restored;

      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(HINT_KEY)).toBeNull();
    });

    it('starts signed out but keeps the flag when the API cannot be reached', async () => {
      localStorage.setItem(HINT_KEY, '1');

      const restored = service.restore();
      httpMock.expectOne(REFRESH_URL).error(new ProgressEvent('error'));
      await restored;

      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(HINT_KEY)).toBe('1');
    });
  });

  describe('refreshing the session', () => {
    it('shares one request between callers that ask at the same time', async () => {
      const calls = [service.refreshSession(), service.refreshSession(), service.refreshSession()];

      httpMock.expectOne(REFRESH_URL).flush({ accessToken: SECOND });

      expect(await Promise.all(calls)).toEqual([SECOND, SECOND, SECOND]);
    });

    it('starts a new request once the last one has finished', async () => {
      const first = service.refreshSession();
      httpMock.expectOne(REFRESH_URL).flush({ accessToken: FIRST });
      await first;

      const second = service.refreshSession();
      httpMock.expectOne(REFRESH_URL).flush({ accessToken: SECOND });

      expect(await second).toBe(SECOND);
      expect(service.currentAccessToken()).toBe(SECOND);
    });

    it('sends the cookie only, with no body', () => {
      void service.refreshSession();

      const request = httpMock.expectOne(REFRESH_URL);

      expect(request.request.body).toBeNull();
      request.flush({ accessToken: FIRST });
    });

    it('ends the session and answers null when the server says it is over', async () => {
      signIn(FIRST);

      const result = service.refreshSession();
      httpMock.expectOne(REFRESH_URL).flush('', UNAUTHORIZED);

      expect(await result).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(HINT_KEY)).toBeNull();
    });

    it('keeps the session and fails the caller when the refresh could not be done', async () => {
      signIn(FIRST);

      const result = service.refreshSession();
      httpMock.expectOne(REFRESH_URL).flush('', { status: 500, statusText: 'Server Error' });

      await expect(result).rejects.toBeDefined();
      expect(service.currentAccessToken()).toBe(FIRST);
      expect(localStorage.getItem(HINT_KEY)).toBe('1');
    });

    it('throws away a refresh that finishes after the session was cleared', async () => {
      signIn(FIRST);

      const result = service.refreshSession();
      service.clearSession();
      httpMock.expectOne(REFRESH_URL).flush({ accessToken: SECOND });

      expect(await result).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(HINT_KEY)).toBeNull();
    });

    it('gives up on a refresh that takes more than 10 seconds', async () => {
      vi.useFakeTimers();
      signIn(FIRST);

      const result = service.refreshSession();
      const request = httpMock.expectOne(REFRESH_URL);
      const failed = expect(result).rejects.toThrow();
      await vi.advanceTimersByTimeAsync(10_000);

      await failed;
      expect(request.cancelled).toBe(true);
      expect(service.currentAccessToken()).toBe(FIRST);
    });
  });

  describe('knowing when the access token is about to run out', () => {
    it('counts the 15 minutes from when the token arrived, whatever its own dates say', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-01-01T10:00:00Z'));
      signIn(FIRST);

      expect(service.isExpiring()).toBe(false);

      vi.advanceTimersByTime(869_000);
      expect(service.isExpiring()).toBe(false);

      vi.advanceTimersByTime(2_000);
      expect(service.isExpiring()).toBe(true);
    });

    it('never calls a token it cannot read expiring, so it cannot cause a refresh storm', () => {
      signIn('not-a-jwt');

      expect(service.isAuthenticated()).toBe(true);
      expect(service.isExpiring()).toBe(false);
    });

    it('is not expiring when nobody is signed in', () => {
      expect(service.isExpiring()).toBe(false);
    });
  });

  describe('signing out', () => {
    it('ends the session on the server first, then clears it here and goes to login', () => {
      signIn(FIRST);

      service.logout().subscribe();
      const request = httpMock.expectOne(LOGOUT_URL);
      expect(service.isAuthenticated()).toBe(true);

      request.flush(null, { status: 204, statusText: 'No Content' });

      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(HINT_KEY)).toBeNull();
      expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    });

    it('stays signed in and reports the failure when the server cannot be reached', () => {
      signIn(FIRST);
      const failed = vi.fn();

      service.logout().subscribe({ error: failed });
      httpMock.expectOne(LOGOUT_URL).error(new ProgressEvent('error'));

      expect(failed).toHaveBeenCalled();
      expect(service.isAuthenticated()).toBe(true);
      expect(router.navigateByUrl).not.toHaveBeenCalled();
    });
  });
});

class FakeChannel {
  static instances: FakeChannel[] = [];

  readonly posted: unknown[] = [];
  closed = false;
  private readonly listeners: ((event: MessageEvent) => void)[] = [];

  constructor(readonly name: string) {
    FakeChannel.instances.push(this);
  }

  addEventListener(_type: string, listener: (event: MessageEvent) => void): void {
    this.listeners.push(listener);
  }

  postMessage(message: unknown): void {
    this.posted.push(message);
  }

  close(): void {
    this.closed = true;
  }

  receive(data: unknown): void {
    this.listeners.forEach((listener) => listener({ data } as MessageEvent));
  }
}

describe('AuthService between tabs', () => {
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    FakeChannel.instances = [];
    vi.stubGlobal('BroadcastChannel', FakeChannel);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    removeFakeLocks();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  function signedInService(): AuthService {
    const service = TestBed.inject(AuthService);
    service.login(CREDENTIALS).subscribe();
    httpMock.expectOne(LOGIN_URL).flush({ accessToken: FIRST });

    return service;
  }

  it('tells the other tabs when it signs out', () => {
    const service = signedInService();

    service.logout().subscribe();
    httpMock.expectOne(LOGOUT_URL).flush(null, { status: 204, statusText: 'No Content' });

    expect(FakeChannel.instances[0].name).toBe('cleared-auth');
    expect(FakeChannel.instances[0].posted).toEqual(['signed-out']);
  });

  it('signs out and goes to login when another tab signs out', () => {
    const service = signedInService();

    FakeChannel.instances[0].receive('signed-out');

    expect(service.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('ignores any other message', () => {
    const service = signedInService();

    FakeChannel.instances[0].receive('hello');

    expect(service.isAuthenticated()).toBe(true);
  });

  it('closes the channel when the app is torn down', () => {
    TestBed.inject(AuthService);

    TestBed.resetTestingModule();

    expect(FakeChannel.instances[0].closed).toBe(true);
  });

  it('waits its turn when another tab is already refreshing', async () => {
    const locks = installFakeLocks();
    const service = TestBed.inject(AuthService);
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const otherTab = withTabLock('cleared-refresh', () => held);

    const refreshing = service.refreshSession();
    await new Promise((resolve) => setTimeout(resolve));
    httpMock.expectNone(REFRESH_URL);

    release();
    await otherTab;
    const request = await vi.waitFor(() => httpMock.expectOne(REFRESH_URL));
    request.flush({ accessToken: FIRST });

    expect(await refreshing).toBe(FIRST);
    expect(locks.requested).toEqual(['cleared-refresh', 'cleared-refresh']);
  });
});
