import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '@core/services/auth.service';

// A 401 means the token is missing, expired, or was rejected — there is no scenario where
// retrying with the same token helps. Clear the session and send the user back to login
// rather than leaving them stuck on a page that will fail every request.
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        auth.clearSession();
        router.navigateByUrl('/login');
      }

      return throwError(() => error);
    }),
  );
};
