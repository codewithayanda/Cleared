import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthResponse } from '@core/models/auth.model';
import { AuthService } from '@core/services/auth.service';
import { Login } from './login';

describe('Login', () => {
  let signIn: Subject<AuthResponse>;
  let attempts: number;

  function configure(): void {
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            login: () => {
              attempts++;

              return (signIn = new Subject<AuthResponse>());
            },
          },
        },
      ],
    });
  }

  beforeEach(() => {
    attempts = 0;
    localStorage.clear();
    configure();
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
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

    const press = () => {
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    };

    // Signs in once, and has the server answer with this error.
    const failWith = (error: HttpErrorResponse, email = 'a@b.co.za') => {
      type('#email', email);
      type('#password', 'secret1234');
      press();
      signIn.error(error);
      fixture.detectChanges();
    };

    const wrongPassword = (email?: string) =>
      failWith(new HttpErrorResponse({ status: 401 }), email);

    return {
      fixture,
      element,
      type,
      failWith,
      wrongPassword,
      alert: () =>
        element.querySelector('.login-alert')?.textContent?.replace(/\s+/g, ' ').trim() ?? null,
      button: () => element.querySelector<HTMLButtonElement>('button[type="submit"]')!,
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

  it('shows a countdown and blocks the button once the fifth wrong password has gone in', () => {
    const { wrongPassword, alert, button } = open();

    for (let attempt = 0; attempt < 4; attempt++) {
      wrongPassword();
    }
    expect(alert()).toBe('Invalid email or password.');
    expect(button().disabled).toBe(false);

    wrongPassword();

    expect(alert()).toBe(
      'Too many wrong passwords. If this account exists, it is locked. You can try again in 15:00.',
    );
    expect(button().disabled).toBe(true);
  });

  it('counts wrong passwords for each email on its own', () => {
    const { wrongPassword, alert } = open();

    for (let attempt = 0; attempt < 4; attempt++) {
      wrongPassword('first@b.co.za');
    }
    wrongPassword('second@b.co.za');

    expect(alert()).toBe('Invalid email or password.');
  });

  it('keeps the lock after a reload, for that email only', () => {
    const first = open();
    for (let attempt = 0; attempt < 5; attempt++) {
      first.wrongPassword();
    }

    TestBed.resetTestingModule();
    configure();
    const afterReload = open();

    afterReload.type('#email', 'a@b.co.za');
    afterReload.fixture.detectChanges();
    expect(afterReload.alert()).toContain('locked. You can try again in');
    expect(afterReload.button().disabled).toBe(true);

    afterReload.type('#email', 'someone.else@b.co.za');
    afterReload.fixture.detectChanges();
    expect(afterReload.alert()).toBeNull();
    expect(afterReload.button().disabled).toBe(false);
  });

  it('counts down by the second and lets the person try again when it reaches zero', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T10:00:00Z'));
    const { fixture, wrongPassword, alert, button } = open();
    for (let attempt = 0; attempt < 5; attempt++) {
      wrongPassword();
    }

    vi.advanceTimersByTime(61_000);
    fixture.detectChanges();
    expect(alert()).toContain('try again in 13:59');

    vi.advanceTimersByTime(14 * 60_000);
    fixture.detectChanges();
    expect(alert()).toBeNull();
    expect(button().disabled).toBe(false);

    wrongPassword();
    expect(alert()).toBe('Invalid email or password.');
  });

  it('does not ask the server while the account is locked', () => {
    const { element, wrongPassword } = open();
    for (let attempt = 0; attempt < 5; attempt++) {
      wrongPassword();
    }
    const before = attempts;

    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(attempts).toBe(before);
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
