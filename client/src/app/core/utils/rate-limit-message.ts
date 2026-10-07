import { HttpErrorResponse } from '@angular/common/http';

// A 429 becomes a sentence a person can act on. Any other error is none of this function's business.
export function rateLimitMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 429) {
    return null;
  }

  const seconds = Number(error.headers.get('Retry-After'));

  return seconds > 0
    ? `Too many attempts. Try again in ${waitFor(seconds)}.`
    : 'Too many attempts. Wait a moment and try again.';
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
