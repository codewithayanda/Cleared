import { HttpErrorResponse } from '@angular/common/http';

export interface RegistrationErrors {
  email: string | null;
  password: string[];
  general: string | null;
}

// Reads the per-field messages the API sends back when it refuses a registration. Anything else,
// such as a network failure or a server error, gives null so the caller can apologise plainly.
export function registrationErrors(error: unknown): RegistrationErrors | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return null;
  }

  const fields = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
  const found: RegistrationErrors = {
    email: fields?.['email']?.[0] ?? null,
    password: fields?.['password'] ?? [],
    general: fields?.['']?.[0] ?? null,
  };

  return found.email || found.password.length > 0 || found.general ? found : null;
}
