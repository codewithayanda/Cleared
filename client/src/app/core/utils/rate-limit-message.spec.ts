import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { rateLimitMessage } from './rate-limit-message';

const refused = (retryAfter?: string) =>
  new HttpErrorResponse({
    status: 429,
    headers: retryAfter === undefined ? undefined : new HttpHeaders({ 'Retry-After': retryAfter }),
  });

describe('rateLimitMessage', () => {
  it('says nothing about an error that is not a 429', () => {
    expect(rateLimitMessage(new HttpErrorResponse({ status: 401 }))).toBeNull();
    expect(rateLimitMessage(new Error('offline'))).toBeNull();
  });

  it('asks the person to wait a moment when the server gave no usable time', () => {
    expect(rateLimitMessage(refused())).toBe('Too many attempts. Wait a moment and try again.');
    expect(rateLimitMessage(refused('soon'))).toBe(
      'Too many attempts. Wait a moment and try again.',
    );
  });

  it.each([
    ['45', 'less than a minute'],
    ['60', '1 minute'],
    ['61', '2 minutes'],
    ['3599', '60 minutes'],
    ['3600', '1 hour'],
    ['7201', '3 hours'],
  ])('turns a Retry-After of %s seconds into "%s"', (seconds, wait) => {
    expect(rateLimitMessage(refused(seconds))).toBe(`Too many attempts. Try again in ${wait}.`);
  });
});
