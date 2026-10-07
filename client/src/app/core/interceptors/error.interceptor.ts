import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, of, switchMap, throwError } from 'rxjs';
import { AuthService } from '@core/services/auth.service';
import { AUTH_ENDPOINT, bearerOf, withBearer } from './auth-context';

const isUnauthorized = (error: unknown): error is HttpErrorResponse =>
  error instanceof HttpErrorResponse && error.status === 401;

// A 401 on an ordinary call usually means the access token ran out, so refresh it and send the
// call again, once. Only when the session itself is over does the user go back to login.
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const endSession = (error: unknown) => {
    auth.clearSession();
    router.navigateByUrl('/login');

    return throwError(() => error);
  };

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error) || req.context.get(AUTH_ENDPOINT)) {
        return throwError(() => error);
      }

      // Another call may already have swapped the token while this one was on its way.
      const current = auth.currentAccessToken();
      const fresh =
        current !== null && current !== bearerOf(req)
          ? of(current)
          : from(auth.refreshSession()).pipe(catchError(() => throwError(() => error)));

      return fresh.pipe(
        switchMap((token) =>
          token === null
            ? endSession(error)
            : next(withBearer(req, token)).pipe(
                catchError((retryError: unknown) =>
                  isUnauthorized(retryError)
                    ? endSession(retryError)
                    : throwError(() => retryError),
                ),
              ),
        ),
      );
    }),
  );
};
