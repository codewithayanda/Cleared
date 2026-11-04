import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '@env';
import { AccountService, ResendResult } from './account.service';

const MINE = `${environment.apiUrl}/users/me`;
const RESEND = `${environment.apiUrl}/users/me/email-verification`;

describe('AccountService', () => {
  let service: AccountService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AccountService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function loaded(emailVerified: boolean) {
    service.load().subscribe();
    httpMock.expectOne(MINE).flush({ email: 'a@b.co.za', emailVerified });
  }

  it('asks the API who is signed in and keeps the answer', () => {
    loaded(false);

    expect(service.account()).toEqual({ email: 'a@b.co.za', emailVerified: false });
  });

  it('forgets the last person while the next one is loading', () => {
    loaded(true);

    service.load().subscribe();

    expect(service.account()).toBeNull();
    httpMock.expectOne(MINE).flush({ email: 'second@b.co.za', emailVerified: false });
    expect(service.account()?.email).toBe('second@b.co.za');
  });

  it('says a link was sent when the API accepts the request, and leaves the account as it was', () => {
    loaded(false);
    let result: ResendResult | null = null;

    service.resendConfirmation().subscribe((value) => (result = value));
    const request = httpMock.expectOne(RESEND);
    request.flush(null, { status: 202, statusText: 'Accepted' });

    expect(request.request.method).toBe('POST');
    expect(result).toBe('sent');
    expect(service.account()?.emailVerified).toBe(false);
  });

  it('says the email is already confirmed on a 204, and updates the account to match', () => {
    loaded(false);
    let result: ResendResult | null = null;

    service.resendConfirmation().subscribe((value) => (result = value));
    httpMock.expectOne(RESEND).flush(null, { status: 204, statusText: 'No Content' });

    expect(result).toBe('already-confirmed');
    expect(service.account()?.emailVerified).toBe(true);
  });

  it('fails, and leaves the account alone, when the API says too many requests', () => {
    loaded(false);
    let failure: unknown = null;

    service.resendConfirmation().subscribe({ error: (error: unknown) => (failure = error) });
    httpMock.expectOne(RESEND).flush(null, { status: 429, statusText: 'Too Many Requests' });

    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect(service.account()?.emailVerified).toBe(false);
  });
});
