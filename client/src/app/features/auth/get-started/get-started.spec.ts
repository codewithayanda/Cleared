import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthResponse } from '@core/models/auth.model';
import { AuthService } from '@core/services/auth.service';
import { GetStarted } from './get-started';

const refused = (errors: Record<string, string[]>) =>
  new HttpErrorResponse({ status: 400, error: { title: 'Registration failed.', errors } });

describe('GetStarted', () => {
  let register: Subject<AuthResponse>;
  let attempts: number;

  beforeEach(() => {
    attempts = 0;
    TestBed.configureTestingModule({
      imports: [GetStarted],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            register: () => {
              attempts++;

              return (register = new Subject<AuthResponse>());
            },
          },
        },
      ],
    });
  });

  function open() {
    const fixture = TestBed.createComponent(GetStarted);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const fill = (selector: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };

    const submit = (password: string) => {
      fill('#companyName', 'Acme');
      fill('#email', 'a@b.co.za');
      fill('#password', password);
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      fixture.detectChanges();
    };

    return { fixture, element, submit, text: () => element.textContent ?? '' };
  }

  it('says an email is taken next to the email box, with a way to sign in', () => {
    const { fixture, submit, text } = open();
    submit('Abcdefghij1');

    register.error(refused({ email: ['An account with this email already exists.'] }));
    fixture.detectChanges();

    expect(text()).toContain('An account with this email already exists.');
    expect(text()).toContain('Sign in instead');
    expect(text()).not.toContain('Could not create');
  });

  it('shows each password rule the server complains about under the password box', () => {
    const { fixture, submit, text } = open();
    submit('Abcdefghij1');

    register.error(refused({ password: ['Passwords must have at least one digit.'] }));
    fixture.detectChanges();

    expect(text()).toContain('Passwords must have at least one digit.');
  });

  it('says how long to wait when the server says too many attempts', () => {
    const { fixture, submit, text } = open();
    submit('Abcdefghij1');

    register.error(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '1800' }) }),
    );
    fixture.detectChanges();

    expect(text()).toContain('Too many attempts. Try again in 30 minutes.');
  });

  it('apologises plainly for anything it cannot explain', () => {
    const { fixture, submit, text } = open();
    submit('Abcdefghij1');

    register.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(text()).toContain('Something went wrong. Please try again.');
  });

  it('does not send a password that breaks the rules the server will check', () => {
    const { fixture, element, submit } = open();

    submit('abcdefghijkl');
    fixture.detectChanges();

    const hint = element.querySelector('p.text-status-overdue');
    expect(attempts).toBe(0);
    expect(hint?.textContent).toContain('with a number and a lowercase letter');
  });
});
