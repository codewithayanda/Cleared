import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EMPTY, Subject } from 'rxjs';
import { Account } from '@core/models/auth.model';
import { AccountService } from '@core/services/account.service';
import { AuthService } from '@core/services/auth.service';
import { Shell } from './shell';

describe('Shell sign out', () => {
  let logout: Subject<void>;

  // Nobody is known yet, so the notice about an unconfirmed email stays out of these tests.
  const accounts = { account: signal<Account | null>(null), load: () => EMPTY };

  beforeEach(() => {
    logout = new Subject<void>();
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { logout: () => logout } },
        { provide: AccountService, useValue: accounts },
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
