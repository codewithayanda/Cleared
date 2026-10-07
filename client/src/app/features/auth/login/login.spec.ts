import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthResponse } from '@core/models/auth.model';
import { AuthService } from '@core/services/auth.service';
import { Login } from './login';

const refused = (retryAfter?: string) =>
  new HttpErrorResponse({
    status: 429,
    headers: retryAfter === undefined ? undefined : new HttpHeaders({ 'Retry-After': retryAfter }),
  });

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

    // Signs in once, and has the server answer with this error.
    const failWith = (error: HttpErrorResponse) => {
      type('#email', 'a@b.co.za');
      type('#password', 'secret1234');
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      signIn.error(error);
      fixture.detectChanges();
    };

    return {
      fixture,
      element,
      failWith,
      alert: () =>
        element.querySelector('.login-alert')?.textContent?.replace(/\s+/g, ' ').trim() ?? null,
      button: () => element.querySelector<HTMLButtonElement>('button[type="submit"]')!,
    };
  }

  it('tells people up front how the lockout works', () => {
    const { element } = open();

    expect(element.textContent).toContain('locked for 15 minutes after 5 wrong passwords');
  });

  it('says the details were wrong each time the server says 401, and counts nothing itself', () => {
    const { failWith, alert, button } = open();

    for (let attempt = 0; attempt < 6; attempt++) {
      failWith(new HttpErrorResponse({ status: 401 }));
    }

    expect(alert()).toBe('Invalid email or password.');
    expect(button().disabled).toBe(false);
  });

  it('counts down whatever the server asks for and blocks the button meanwhile', () => {
    const { failWith, alert, button } = open();

    failWith(refused('900'));

    expect(alert()).toBe('Too many attempts. You can try again in 15:00.');
    expect(button().disabled).toBe(true);
  });

  it('counts down by the second and lets the person try again when it reaches zero', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T10:00:00Z'));
    const { fixture, failWith, alert, button } = open();
    failWith(refused('900'));

    vi.advanceTimersByTime(61_000);
    fixture.detectChanges();
    expect(alert()).toContain('try again in 13:59');

    vi.advanceTimersByTime(14 * 60_000);
    fixture.detectChanges();
    expect(alert()).toBeNull();
    expect(button().disabled).toBe(false);

    failWith(new HttpErrorResponse({ status: 401 }));
    expect(alert()).toBe('Invalid email or password.');
  });

  it('does not ask the server again while it is counting down', () => {
    const { element, failWith } = open();
    failWith(refused('900'));
    const before = attempts;

    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(attempts).toBe(before);
  });

  it('asks for a moment, with no countdown, when the server gives no time', () => {
    const { failWith, alert, button } = open();

    failWith(refused());

    expect(alert()).toBe('Too many attempts. Wait a moment and try again.');
    expect(button().disabled).toBe(false);
  });

  it('keeps nothing in the browser, so a reload starts clean and the server decides', () => {
    const { failWith } = open();
    failWith(new HttpErrorResponse({ status: 401 }));
    failWith(refused('900'));

    expect(localStorage.length).toBe(0);

    TestBed.resetTestingModule();
    configure();
    const afterReload = open();

    expect(afterReload.alert()).toBeNull();
    expect(afterReload.button().disabled).toBe(false);
  });

  it('stays calm about anything else', () => {
    const { failWith, alert } = open();

    failWith(new HttpErrorResponse({ status: 500 }));

    expect(alert()).toBe('Something went wrong. Please try again.');
  });
});
