import { HttpErrorResponse } from '@angular/common/http';

// True when the API turned an emailed link away as unknown, used, expired or the wrong shape. Every one
// of those reads the same, so the page can say one thing about all of them.
export function isDeadLink(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return false;
  }

  const fields = (error.error as { errors?: Record<string, string[]> } | null)?.errors;

  return (fields?.['token']?.length ?? 0) > 0;
}
