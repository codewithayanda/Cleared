import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { AuthService } from '@core/services/auth.service';
import { AUTH_ENDPOINT, withBearer } from './auth-context';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(AUTH_ENDPOINT)) {
    return next(req);
  }

  const auth = inject(AuthService);

  if (!auth.isExpiring()) {
    return next(withBearer(req, auth.currentAccessToken()));
  }

  // Refreshing first saves a call that would fail. If it cannot be done the call goes out anyway,
  // because the server is the one that decides whether the token is still good.
  return from(auth.refreshSession().catch(() => null)).pipe(
    switchMap(() => next(withBearer(req, auth.currentAccessToken()))),
  );
};
