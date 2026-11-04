import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { ForgotPasswordRequest } from '@core/models/auth.model';
import { AuthService } from '@core/services/auth.service';
import { ForgotPassword } from './forgot-password';

describe('ForgotPassword', () => {
  let request: Subject<void>;
  let sent: ForgotPasswordRequest[];

  beforeEach(() => {
    sent = [];
    TestBed.configureTestingModule({
      imports: [ForgotPassword],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            forgotPassword: (body: ForgotPasswordRequest) => {
              sent.push(body);

              return (request = new Subject<void>());
            },
          },
        },
      ],
    });
  });

  function open() {
    const fixture = TestBed.createComponent(ForgotPassword);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const submit = (email: string) => {
      const input = element.querySelector<HTMLInputElement>('#email')!;
      input.value = email;
      input.dispatchEvent(new Event('input'));
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      fixture.detectChanges();
    };

    return { fixture, element, submit, text: () => element.textContent ?? '' };
  }

  it('sends only the email, then says a link goes to that address if it has an account', () => {
    const { fixture, submit, text, element } = open();

    submit('a@b.co.za');
    request.next();
    request.complete();
    fixture.detectChanges();

    expect(sent).toEqual([{ email: 'a@b.co.za' }]);
    expect(text()).toContain('If that address has an account, we have sent it a link');
    expect(text()).toContain('works for 30 minutes, and only once');
    expect(element.querySelector('form')).toBeNull();
  });

  it('does not say a link was sent until the server has answered', () => {
    const { submit, text } = open();

    submit('a@b.co.za');

    expect(text()).not.toContain('we have sent it a link');
  });

  it.each(['not-an-email', 'a@b'])('sends nothing and says why when the email is %s', (email) => {
    const { submit, text } = open();

    submit(email);

    expect(sent).toHaveLength(0);
    expect(text()).toContain('Enter a valid email address.');
  });

  it('says how long to wait when the server says too many requests', () => {
    const { fixture, submit, text } = open();

    submit('a@b.co.za');
    request.error(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '3500' }) }),
    );
    fixture.detectChanges();

    expect(text()).toContain('Too many attempts. Try again in 59 minutes.');
    expect(text()).not.toContain('we have sent it a link');
  });

  it('apologises plainly for anything it cannot explain, and lets the person try again', () => {
    const { fixture, submit, text, element } = open();

    submit('a@b.co.za');
    request.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(text()).toContain('Something went wrong. Please try again.');
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
  });

  it('links back to sign in', () => {
    const { element } = open();

    expect(element.querySelector('a[href="/login"]')).not.toBeNull();
  });
});
