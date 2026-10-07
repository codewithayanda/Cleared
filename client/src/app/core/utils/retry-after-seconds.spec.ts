import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { retryAfterSeconds } from './rate-limit-message';

const refused = (retryAfter?: string) =>
  new HttpErrorResponse({
    status: 429,
    headers: retryAfter === undefined ? undefined : new HttpHeaders({ 'Retry-After': retryAfter }),
  });

describe('retryAfterSeconds', () => {
  it('reads the time the server asks for', () => {
    expect(retryAfterSeconds(refused('900'))).toBe(900);
  });

  it('gives null when a 429 carries no usable time', () => {
    expect(retryAfterSeconds(refused())).toBeNull();
    expect(retryAfterSeconds(refused('soon'))).toBeNull();
    expect(retryAfterSeconds(refused('0'))).toBeNull();
  });

  it('gives null for any other error', () => {
    expect(retryAfterSeconds(new HttpErrorResponse({ status: 401 }))).toBeNull();
    expect(retryAfterSeconds(new Error('offline'))).toBeNull();
  });
});
