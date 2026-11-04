import { HttpErrorResponse } from '@angular/common/http';
import { passwordResetErrors } from './password-reset-errors';

const refused = (errors: Record<string, string[]>) =>
  new HttpErrorResponse({ status: 400, error: { title: 'Password reset failed.', errors } });

describe('passwordResetErrors', () => {
  it('reads a link that is no good', () => {
    const found = passwordResetErrors(refused({ token: ['This link is invalid or has expired.'] }));

    expect(found).toEqual({ link: 'This link is invalid or has expired.', password: [] });
  });

  it('keeps every rule the password broke, in order', () => {
    const found = passwordResetErrors(refused({ password: ['Too short.', 'Needs a digit.'] }));

    expect(found).toEqual({ link: null, password: ['Too short.', 'Needs a digit.'] });
  });

  it('gives null for anything that is not a field error', () => {
    expect(passwordResetErrors(new HttpErrorResponse({ status: 500 }))).toBeNull();
    expect(passwordResetErrors(new HttpErrorResponse({ status: 429 }))).toBeNull();
    expect(passwordResetErrors(new HttpErrorResponse({ status: 400, error: 'nope' }))).toBeNull();
    expect(passwordResetErrors(refused({ email: ['Required.'] }))).toBeNull();
    expect(passwordResetErrors(new Error('offline'))).toBeNull();
  });
});
