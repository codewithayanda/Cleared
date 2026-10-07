import { FormControl } from '@angular/forms';
import { passwordRules } from './password-rules';

const check = (value: string) => passwordRules(new FormControl(value));

describe('passwordRules', () => {
  it('accepts a password with a number and a lowercase letter', () => {
    expect(check('Abcdefghij1')).toBeNull();
  });

  it('rejects one with no number', () => {
    expect(check('abcdefghijkl')).toEqual({ passwordRules: true });
  });

  it('rejects one with no lowercase letter', () => {
    expect(check('ABCDEFGHIJ12')).toEqual({ passwordRules: true });
  });
});
