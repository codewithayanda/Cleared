import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '@env';
import { AuthService } from './auth.service';

// A real (expired, harmless) HS256 token shaped like the ones the API issues, so decoding
// logic is exercised against actual JWT structure rather than a hand-typed fake string.
// Header: {"alg":"HS256","typ":"JWT"}, Payload: {"sub":"u1","tenant_id":"tenant-42","exp":1}
const SAMPLE_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJ1MSIsInRlbmFudF9pZCI6InRlbmFudC00MiIsImV4cCI6MX0' +
  '.dummy-signature';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('is not authenticated before any login', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentAccessToken()).toBeNull();
  });

  it('becomes authenticated once login succeeds, storing the token in memory only', () => {
    service.login({ email: 'a@b.co.za', password: 'secret1234' }).subscribe();

    httpMock.expectOne(`${environment.apiUrl}/auth/login`).flush({ accessToken: SAMPLE_TOKEN });

    expect(service.isAuthenticated()).toBe(true);
    expect(service.currentAccessToken()).toBe(SAMPLE_TOKEN);
  });

  it('clearSession logs the user out without navigating', () => {
    service.login({ email: 'a@b.co.za', password: 'secret1234' }).subscribe();
    httpMock.expectOne(`${environment.apiUrl}/auth/login`).flush({ accessToken: SAMPLE_TOKEN });

    service.clearSession();

    expect(service.isAuthenticated()).toBe(false);
  });

  it('register also establishes a session from the returned token', () => {
    service
      .register({ tenantId: 'tenant-42', email: 'a@b.co.za', password: 'secret1234' })
      .subscribe();

    httpMock.expectOne(`${environment.apiUrl}/auth/register`).flush({ accessToken: SAMPLE_TOKEN });

    expect(service.isAuthenticated()).toBe(true);
  });
});
