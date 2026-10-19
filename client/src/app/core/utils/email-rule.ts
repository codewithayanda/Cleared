import { ValidatorFn } from '@angular/forms';

const MAX_LENGTH = 254;
const MAX_LOCAL_PART_LENGTH = 64;
const LOCAL_PART = /^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*$/;
const DOMAIN_LABEL = /^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/;

// The same rule as EmailAddressRule in the API, so a form refuses what the server would.
export function isValidEmail(value: string): boolean {
  const at = value.indexOf('@');

  if (value.length > MAX_LENGTH || at < 1 || at !== value.lastIndexOf('@')) {
    return false;
  }

  const localPart = value.slice(0, at);
  const labels = value.slice(at + 1).split('.');
  const topLevel = labels[labels.length - 1];

  return (
    localPart.length <= MAX_LOCAL_PART_LENGTH &&
    LOCAL_PART.test(localPart) &&
    labels.length >= 2 &&
    labels.every((label) => DOMAIN_LABEL.test(label)) &&
    topLevel.length >= 2 &&
    !/^\d+$/.test(topLevel)
  );
}

// An empty box passes, so the field can be optional or sit beside Validators.required. An email
// box already hands over its value without spaces around it, so none are forgiven here.
export const validEmail: ValidatorFn = (control) => {
  const value = String(control.value ?? '');

  return value === '' || isValidEmail(value) ? null : { email: true };
};
