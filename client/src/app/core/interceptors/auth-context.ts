import { HttpContext, HttpContextToken, HttpRequest } from '@angular/common/http';

// Calls to the auth endpoints never trigger a refresh: a 401 there is an answer, not an expiry.
export const AUTH_ENDPOINT = new HttpContextToken<boolean>(() => false);

export function authEndpoint(): HttpContext {
  return new HttpContext().set(AUTH_ENDPOINT, true);
}

export function withBearer<T>(req: HttpRequest<T>, token: string | null): HttpRequest<T> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

export function bearerOf(req: HttpRequest<unknown>): string | null {
  return req.headers.get('Authorization')?.replace('Bearer ', '') ?? null;
}
