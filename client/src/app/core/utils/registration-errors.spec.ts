import { HttpErrorResponse } from '@angular/common/http';
import { registrationErrors } from './registration-errors';

const refused = (errors: Record<string, string[]>) =>
  new HttpErrorResponse({ status: 400, error: { title: 'Registration failed.', errors } });

describe('registrationErrors', () => {
  it('reads a taken email', () => {
    const found = registrationErrors(
      refused({ email: ['An account with this email already exists.'] }),
    );

    expect(found).toEqual({
      email: 'An account with this email already exists.',
      password: [],
      general: null,
    });
  });

  it('keeps every password message, in order', () => {
    const found = registrationErrors(refused({ password: ['Too short.', 'Needs a digit.'] }));

    expect(found?.password).toEqual(['Too short.', 'Needs a digit.']);
  });

  it('reads an error that belongs to no field', () => {
    expect(registrationErrors(refused({ '': ['Something odd.'] }))?.general).toBe('Something odd.');
  });

  it('gives null for anything that is not a field error', () => {
    expect(registrationErrors(new HttpErrorResponse({ status: 500 }))).toBeNull();
    expect(registrationErrors(new HttpErrorResponse({ status: 400, error: 'nope' }))).toBeNull();
    expect(registrationErrors(refused({ company: ['Required.'] }))).toBeNull();
    expect(registrationErrors(new Error('offline'))).toBeNull();
  });
});
