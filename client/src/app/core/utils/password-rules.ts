import { ValidatorFn } from '@angular/forms';

// Mirrors the password options in the API's Program.cs, so people see the rules before they submit.
// H3.5 replaces both with one proper policy.
export const passwordRules: ValidatorFn = (control) => {
  const value = String(control.value ?? '');

  return /\d/.test(value) && /[a-z]/.test(value) ? null : { passwordRules: true };
};
