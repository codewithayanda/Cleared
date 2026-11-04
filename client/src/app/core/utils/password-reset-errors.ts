import { HttpErrorResponse } from '@angular/common/http';

export interface PasswordResetErrors {
  link: string | null;
  password: string[];
}

// Reads the per-field messages the API sends back when it refuses a reset: one about the link, or the
// rules the new password broke. Anything else gives null so the caller can apologise plainly.
export function passwordResetErrors(error: unknown): PasswordResetErrors | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return null;
  }

  const fields = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
  const found: PasswordResetErrors = {
    link: fields?.['token']?.[0] ?? null,
    password: fields?.['password'] ?? [],
  };

  return found.link || found.password.length > 0 ? found : null;
}
