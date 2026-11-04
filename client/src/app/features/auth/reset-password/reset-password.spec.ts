import { Location } from '@angular/common';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject } from 'rxjs';
import { ResetPasswordRequest } from '@core/models/auth.model';
import { AuthService } from '@core/services/auth.service';
import { ResetPassword } from './reset-password';

const TOKEN = 'A'.repeat(43);

const refused = (errors: Record<string, string[]>) =>
  new HttpErrorResponse({ status: 400, error: { title: 'Password reset failed.', errors } });

describe('ResetPassword', () => {
  let request: Subject<void>;
  let sent: ResetPasswordRequest[];

  // The fake has nothing but resetPassword, so a call to login or refresh would throw.
  beforeEach(() => {
    sent = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'reset-password', component: ResetPassword }]),
        {
          provide: AuthService,
          useValue: {
            resetPassword: (body: ResetPasswordRequest) => {
              sent.push(body);

              return (request = new Subject<void>());
            },
          },
        },
      ],
    });
  });

  async function open(url = `/reset-password#${TOKEN}`) {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, ResetPassword);
    const element = harness.routeNativeElement as HTMLElement;

    const submit = (password: string) => {
      const input = element.querySelector<HTMLInputElement>('#password')!;
      input.value = password;
      input.dispatchEvent(new Event('input'));
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      harness.detectChanges();
    };

    return { harness, element, submit, text: () => element.textContent ?? '' };
  }

  it('sends the secret from the link with the new password, and says it was changed', async () => {
    const { harness, element, submit, text } = await open();

    submit('Abcdefghij1');
    request.next();
    request.complete();
    harness.detectChanges();

    expect(sent).toEqual([{ token: TOKEN, newPassword: 'Abcdefghij1' }]);
    expect(text()).toContain('Your password was changed');
    expect(element.querySelector('a[href="/login"]')).not.toBeNull();
    expect(element.querySelector('form')).toBeNull();
  });

  it('takes the secret out of the address bar as soon as it has read it', async () => {
    await open();

    expect(TestBed.inject(Location).path(true)).toBe('/reset-password');
  });

  it('says the link cannot be used, with no form, when the page is opened without one', async () => {
    const { element, text } = await open('/reset-password');

    expect(text()).toContain('This link is invalid or has expired');
    expect(element.querySelector('form')).toBeNull();
    expect(element.querySelector('a[href="/forgot-password"]')).not.toBeNull();
    expect(sent).toHaveLength(0);
  });

  it('does not send a password that breaks the rules the server will check', async () => {
    const { element, submit } = await open();

    submit('abcdefghijkl');

    expect(sent).toHaveLength(0);
    expect(element.querySelector('p.text-status-overdue')?.textContent).toContain(
      'with a number and a lowercase letter',
    );
  });

  it('shows the rules the server says were broken and keeps the form for another try', async () => {
    const { harness, element, submit, text } = await open();

    submit('Abcdefghij1');
    request.error(refused({ password: ['Passwords must have at least one digit.'] }));
    harness.detectChanges();

    expect(text()).toContain('Passwords must have at least one digit.');
    expect(text()).not.toContain('This link is invalid');
    expect(element.querySelector('form')).not.toBeNull();
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
  });

  it('says the link cannot be used when the server says it has expired or was used', async () => {
    const { harness, element, submit, text } = await open();

    submit('Abcdefghij1');
    request.error(refused({ token: ['This link is invalid or has expired. Ask for a new one.'] }));
    harness.detectChanges();

    expect(text()).toContain('This link is invalid or has expired');
    expect(element.querySelector('form')).toBeNull();
    expect(element.querySelector('a[href="/forgot-password"]')).not.toBeNull();
  });

  it('says how long to wait when the server says too many requests', async () => {
    const { harness, submit, text } = await open();

    submit('Abcdefghij1');
    request.error(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '1800' }) }),
    );
    harness.detectChanges();

    expect(text()).toContain('Too many attempts. Try again in 30 minutes.');
  });

  it('apologises plainly for anything it cannot explain', async () => {
    const { harness, submit, text } = await open();

    submit('Abcdefghij1');
    request.error(new HttpErrorResponse({ status: 500 }));
    harness.detectChanges();

    expect(text()).toContain('Something went wrong. Please try again.');
  });
});
