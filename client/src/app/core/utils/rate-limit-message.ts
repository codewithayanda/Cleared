import { HttpErrorResponse } from '@angular/common/http';

// How many seconds the server says to wait after a 429, or null for any other error or a 429 that
// gave no usable time.
export function retryAfterSeconds(error: unknown): number | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 429) {
    return null;
  }

  const seconds = Number(error.headers.get('Retry-After'));

  return seconds > 0 ? seconds : null;
}

// A 429 becomes a sentence a person can act on. Any other error is none of this function's business.
export function rateLimitMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 429) {
    return null;
  }

  const seconds = retryAfterSeconds(error);

  return seconds === null
    ? 'Too many attempts. Wait a moment and try again.'
    : `Too many attempts. Try again in ${waitFor(seconds)}.`;
}

function waitFor(seconds: number): string {
  if (seconds < 60) {
    return 'less than a minute';
  }

  return seconds < 3600
    ? plural(Math.ceil(seconds / 60), 'minute')
    : plural(Math.ceil(seconds / 3600), 'hour');
}

const plural = (count: number, unit: string): string => `${count} ${unit}${count === 1 ? '' : 's'}`;
