import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EMPTY, Subject } from 'rxjs';
import { Account } from '@core/models/auth.model';
import { AccountService } from '@core/services/account.service';
import { AuthService } from '@core/services/auth.service';
import { IdleService } from '@core/services/idle.service';
import { Shell } from './shell';

describe('Shell sign out', () => {
  let logout: Subject<void>;
  let start: ReturnType<typeof vi.fn>;
  let stop: ReturnType<typeof vi.fn>;

  // Nobody is known yet, so the notice about an unconfirmed email stays out of these tests.
  const accounts = { account: signal<Account | null>(null), load: () => EMPTY };

  beforeEach(() => {
    logout = new Subject<void>();
    start = vi.fn();
    stop = vi.fn();
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { logout: () => logout } },
        { provide: AccountService, useValue: accounts },
        {
          provide: IdleService,
          useValue: {
            start,
            stop,
            warning: signal(false),
            secondsLeft: signal(0),
            staySignedIn: vi.fn(),
            signOutNow: vi.fn(),
          },
        },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const button = element.querySelector<HTMLButtonElement>('button[type="button"]')!;

    return { fixture, button, text: () => element.textContent ?? '' };
  }

  it('starts watching for inactivity when it opens, and stops when it goes', () => {
    const { fixture } = render();

    expect(start).toHaveBeenCalledTimes(1);
    expect(stop).not.toHaveBeenCalled();

    fixture.destroy();

    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('carries the inactivity warning, which shows itself only when it is needed', () => {
    const { fixture } = render();

    expect(fixture.nativeElement.querySelector('app-idle-warning')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('shows nothing wrong before anything has happened', () => {
    const { button, text } = render();

    expect(button.disabled).toBe(false);
    expect(text()).toContain('Sign out');
    expect(text()).not.toContain('Could not sign out');
  });

  it('says it is signing out and blocks a second click while the server answers', () => {
    const { fixture, button, text } = render();

    button.click();
    fixture.detectChanges();

    expect(button.disabled).toBe(true);
    expect(text()).toContain('Signing out...');
  });

  it('says so and lets the person try again when the server could not be reached', () => {
    const { fixture, button, text } = render();

    button.click();
    logout.error(new Error('offline'));
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
    expect(text()).toContain('Could not sign out');
  });
});
