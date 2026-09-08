import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '@core/services/auth.service';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  function runGuard(isAuthenticated: boolean) {
    const authServiceStub = { isAuthenticated: () => isAuthenticated };

    TestBed.overrideProvider(AuthService, { useValue: authServiceStub });

    return TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/invoices' } as never),
    );
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { isAuthenticated: () => false } }],
    });
  });

  it('allows navigation when the user is authenticated', () => {
    const result = runGuard(true);

    expect(result).toBe(true);
  });

  it('redirects to /login when the user is not authenticated', () => {
    const result = runGuard(false);
    const router = TestBed.inject(Router);

    expect(result).toEqual(router.createUrlTree(['/login']));
  });
});
