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
    signIn = new Subject<AuthResponse>();
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), { provide: AuthService, useValue: { login: () => signIn } }],
    });
  });

  function type(element: HTMLElement, selector: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit() {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    type(element, '#email', 'a@b.co.za');
    type(element, '#password', 'secret1234');
    element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();

    return {
      fixture,
      alert: () => element.querySelector('.login-alert')?.textContent?.trim() ?? null,
    };
  }

  it('tells the person the details were wrong when the server says 401', () => {
    const { fixture, alert } = submit();

    signIn.error(new HttpErrorResponse({ status: 401 }));
    fixture.detectChanges();

    expect(alert()).toBe('Invalid email or password.');
  });

  it('says how long to wait when the server says too many attempts', () => {
    const { fixture, alert } = submit();

    signIn.error(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '45' }) }),
    );
    fixture.detectChanges();

    expect(alert()).toBe('Too many attempts. Try again in less than a minute.');
  });

  it('stays calm about anything else', () => {
    const { fixture, alert } = submit();

    signIn.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(alert()).toBe('Something went wrong. Please try again.');
  });
});
