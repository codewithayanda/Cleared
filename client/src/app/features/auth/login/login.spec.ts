import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthResponse } from '@core/models/auth.model';
import { AuthService } from '@core/services/auth.service';
import { Login } from './login';

describe('Login', () => {
  let signIn: Subject<AuthResponse>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login: () => (signIn = new Subject<AuthResponse>()) } },
      ],
    });
  });

  function open() {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const type = (selector: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };

    // Signs in once, and has the server answer with this error.
    const failWith = (error: HttpErrorResponse, email = 'a@b.co.za') => {
      type('#email', email);
      type('#password', 'secret1234');
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      signIn.error(error);
      fixture.detectChanges();
    };

    const wrongPassword = (email?: string) =>
      failWith(new HttpErrorResponse({ status: 401 }), email);

    return {
      element,
      failWith,
      wrongPassword,
      alert: () => element.querySelector('.login-alert')?.textContent?.trim() ?? null,
    };
  }

  it('tells people up front how the lockout works', () => {
    const { element } = open();

    expect(element.textContent).toContain('locked for 15 minutes after 5 wrong passwords');
  });

  it('tells the person the details were wrong when the server says 401', () => {
    const { wrongPassword, alert } = open();

    wrongPassword();

    expect(alert()).toBe('Invalid email or password.');
  });

  it('says the account may be locked once the fifth wrong password has gone in', () => {
    const { wrongPassword, alert } = open();

    for (let attempt = 0; attempt < 4; attempt++) {
      wrongPassword();
    }
    expect(alert()).toBe('Invalid email or password.');

    wrongPassword();

    expect(alert()).toBe(
      'Too many wrong passwords. If this account exists, it is locked for 15 minutes. Wait, then try again.',
    );
  });

  it('counts wrong passwords for each email on its own', () => {
    const { wrongPassword, alert } = open();

    for (let attempt = 0; attempt < 4; attempt++) {
      wrongPassword('first@b.co.za');
    }
    wrongPassword('second@b.co.za');

    expect(alert()).toBe('Invalid email or password.');
  });

  it('says how long to wait when the server says too many attempts', () => {
    const { failWith, alert } = open();

    failWith(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '45' }) }),
    );

    expect(alert()).toBe('Too many attempts. Try again in less than a minute.');
  });

  it('stays calm about anything else', () => {
    const { failWith, alert } = open();

    failWith(new HttpErrorResponse({ status: 500 }));

    expect(alert()).toBe('Something went wrong. Please try again.');
  });
});
