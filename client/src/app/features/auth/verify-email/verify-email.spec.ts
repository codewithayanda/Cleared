import { Location } from '@angular/common';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of } from 'rxjs';
import { VerifyEmailRequest } from '@core/models/auth.model';
import { AccountService } from '@core/services/account.service';
import { AuthService } from '@core/services/auth.service';
import { VerifyEmail } from './verify-email';

const TOKEN = 'A'.repeat(43);

const deadLink = () =>
  new HttpErrorResponse({
    status: 400,
    error: {
      title: 'Email confirmation failed.',
      errors: { token: ['This link is invalid or has expired. Ask for a new one.'] },
    },
  });

describe('VerifyEmail', () => {
  let request: Subject<void>;
  let sent: VerifyEmailRequest[];
  let signedIn: boolean;
  let loads: number;

  // The fake has nothing but verifyEmail and isAuthenticated, so a call to anything else would throw.
  beforeEach(() => {
    sent = [];
    signedIn = false;
    loads = 0;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'verify-email', component: VerifyEmail }]),
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => signedIn,
            verifyEmail: (body: VerifyEmailRequest) => {
              sent.push(body);

              return (request = new Subject<void>());
            },
          },
        },
        {
          provide: AccountService,
          useValue: {
            load: () => {
              loads++;

              return of({ email: 'a@b.co.za', emailVerified: true });
            },
          },
        },
      ],
    });
  });

  async function open(url = `/verify-email#${TOKEN}`) {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, VerifyEmail);
    const element = harness.routeNativeElement as HTMLElement;

    const confirm = () => {
      element.querySelector<HTMLButtonElement>('button[type="button"]')!.click();
      harness.detectChanges();
    };

    return { harness, element, confirm, text: () => element.textContent ?? '' };
  }

  it('confirms nothing just by being opened, which is all a mail scanner does', async () => {
    const { element, text } = await open();

    expect(sent).toHaveLength(0);
    expect(text()).toContain('Confirm your email address');
    expect(element.querySelector('button[type="button"]')).not.toBeNull();
  });

  it('takes the secret out of the address bar as soon as it has read it', async () => {
    await open();

    expect(TestBed.inject(Location).path(true)).toBe('/verify-email');
  });

  it('sends the secret when the button is pressed, and says the email is confirmed', async () => {
    const { harness, element, confirm, text } = await open();

    confirm();
    request.next();
    request.complete();
    harness.detectChanges();

    expect(sent).toEqual([{ token: TOKEN }]);
    expect(text()).toContain('Email confirmed');
    expect(element.querySelector('a[href="/"]')).not.toBeNull();
    expect(element.querySelector('button[type="button"]')).toBeNull();
  });

  it('says the link cannot be used, with no button, when the page is opened without one', async () => {
    const { element, text } = await open('/verify-email');

    expect(text()).toContain('This link is invalid or has expired');
    expect(element.querySelector('button[type="button"]')).toBeNull();
    expect(sent).toHaveLength(0);
  });

  it('says the link cannot be used when the server says it has expired or was used', async () => {
    const { harness, element, confirm, text } = await open();

    confirm();
    request.error(deadLink());
    harness.detectChanges();

    expect(text()).toContain('This link cannot be used');
    expect(element.querySelector('button[type="button"]')).toBeNull();
  });

  it('says how long to wait when the server says too many requests', async () => {
    const { harness, element, confirm, text } = await open();

    confirm();
    request.error(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '1800' }) }),
    );
    harness.detectChanges();

    expect(text()).toContain('Too many attempts. Try again in 30 minutes.');
    expect(element.querySelector<HTMLButtonElement>('button[type="button"]')!.disabled).toBe(false);
  });

  it('apologises plainly for anything it cannot explain', async () => {
    const { harness, confirm, text } = await open();

    confirm();
    request.error(new HttpErrorResponse({ status: 500 }));
    harness.detectChanges();

    expect(text()).toContain('Something went wrong. Please try again.');
  });

  it('refreshes the notice at the top of the app for someone who is signed in', async () => {
    signedIn = true;
    const { harness, confirm } = await open();

    confirm();
    request.next();
    request.complete();
    harness.detectChanges();

    expect(loads).toBe(1);
  });

  it('does not ask the API about an account for someone who is not signed in', async () => {
    const { harness, confirm } = await open();

    confirm();
    request.next();
    request.complete();
    harness.detectChanges();

    expect(loads).toBe(0);
  });
});
