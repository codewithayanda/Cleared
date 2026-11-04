import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import { Account } from '@core/models/auth.model';
import { AccountService, ResendResult } from '@core/services/account.service';
import { VerifyEmailBanner } from './verify-email-banner';

const unconfirmed: Account = { email: 'a@b.co.za', emailVerified: false };

describe('VerifyEmailBanner', () => {
  let account: WritableSignal<Account | null>;
  let resend: Subject<ResendResult>;
  let loads: number;

  beforeEach(() => {
    account = signal<Account | null>(null);
    loads = 0;
    TestBed.configureTestingModule({
      imports: [VerifyEmailBanner],
      providers: [
        {
          provide: AccountService,
          useValue: {
            account,
            load: () => {
              loads++;

              return EMPTY;
            },
            resendConfirmation: () => (resend = new Subject<ResendResult>()),
          },
        },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(VerifyEmailBanner);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const click = () => {
      element.querySelector<HTMLButtonElement>('button')!.click();
      fixture.detectChanges();
    };

    return {
      fixture,
      element,
      click,
      button: () => element.querySelector<HTMLButtonElement>('button'),
      text: () => element.textContent ?? '',
    };
  }

  it('asks for the account when it appears', () => {
    render();

    expect(loads).toBe(1);
  });

  it('says nothing before the account is known', () => {
    const { element } = render();

    expect(element.querySelector('[role="status"]')).toBeNull();
  });

  it('says nothing once the email is confirmed', () => {
    account.set({ ...unconfirmed, emailVerified: true });

    const { element } = render();

    expect(element.querySelector('[role="status"]')).toBeNull();
  });

  it('asks someone with an unconfirmed email to confirm it, naming the address', () => {
    account.set(unconfirmed);

    const { text } = render();

    expect(text()).toContain('Confirm your email address');
    expect(text()).toContain('a@b.co.za');
  });

  it('says it is sending, blocks a second click, then says the link was sent', () => {
    account.set(unconfirmed);
    const { fixture, click, button, text } = render();

    click();

    expect(button()!.disabled).toBe(true);
    expect(text()).toContain('Sending…');

    resend.next('sent');
    resend.complete();
    fixture.detectChanges();

    expect(button()!.disabled).toBe(false);
    expect(text()).toContain('Sent. Check your email.');
  });

  it('says how long to wait when the server says too many requests', () => {
    account.set(unconfirmed);
    const { fixture, click, text } = render();

    click();
    resend.error(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '1800' }) }),
    );
    fixture.detectChanges();

    expect(text()).toContain('Too many attempts. Try again in 30 minutes.');
    expect(text()).not.toContain('Sent.');
  });

  it('apologises plainly for anything it cannot explain', () => {
    account.set(unconfirmed);
    const { fixture, click, text } = render();

    click();
    resend.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(text()).toContain('Could not send the link. Please try again.');
  });

  it('goes away when the server says the email is already confirmed', () => {
    account.set(unconfirmed);
    const { fixture, element, click } = render();

    click();
    account.set({ ...unconfirmed, emailVerified: true });
    resend.next('already-confirmed');
    resend.complete();
    fixture.detectChanges();

    expect(element.querySelector('[role="status"]')).toBeNull();
  });
});
