import { FormControl } from '@angular/forms';
import { isValidEmail, validEmail } from './email-rule';

// Keep these two lists in step with EmailAddressRuleTests in the API's domain tests.
const real = [
  'jane@example.co.za',
  'jane.doe@example.com',
  'jane+invoices@example.com',
  "o'brien@example.co.za",
  'a_b-c@sub.example.org',
  '1234567890@example.com',
  'x@y.zz',
  'jane@xn--80ak6aa92e.com',
];

const notAddresses = [
  '',
  ' ',
  'not-an-email',
  'jane@example',
  'jane@localhost',
  '@example.com',
  'jane@',
  'jane@@example.com',
  'jane@home@example.com',
  'jane doe@example.com',
  ' jane@example.com',
  'jane@example.com ',
  'jane@example.com\n',
  'jane@example..com',
  'jane@.example.com',
  'jane@example.com.',
  '.jane@example.com',
  'jane.@example.com',
  'ja..ne@example.com',
  'jane@-example.com',
  'jane@example-.com',
  'jane@example.c',
  'jane@192.168.1.100',
  'jane@[192.168.1.100]',
  'josé@example.com',
  'jane@exämple.com',
  '"jane doe"@example.com',
  'jane,doe@example.com',
  'jane(doe)@example.com',
];

function addressOfLength(length: number): string {
  const slack = length - 64 - 1 - 63 - 1 - 63 - 1 - '.com'.length;

  return `${'a'.repeat(64)}@${'b'.repeat(63)}.${'c'.repeat(63)}.${'d'.repeat(slack)}.com`;
}

describe('isValidEmail', () => {
  it.each(real)('accepts %s', (email) => {
    expect(isValidEmail(email)).toBe(true);
  });

  it.each(notAddresses)('refuses %j', (email) => {
    expect(isValidEmail(email)).toBe(false);
  });

  it('accepts exactly 254 characters and refuses one more', () => {
    expect(addressOfLength(254)).toHaveLength(254);
    expect(isValidEmail(addressOfLength(254))).toBe(true);
    expect(isValidEmail(addressOfLength(255))).toBe(false);
  });

  it('limits the part before the @ to 64 characters and each domain label to 63', () => {
    expect(isValidEmail(`${'a'.repeat(64)}@example.com`)).toBe(true);
    expect(isValidEmail(`${'a'.repeat(65)}@example.com`)).toBe(false);
    expect(isValidEmail(`jane@${'b'.repeat(63)}.com`)).toBe(true);
    expect(isValidEmail(`jane@${'b'.repeat(64)}.com`)).toBe(false);
  });
});

describe('validEmail', () => {
  const check = (value: string | null) => validEmail(new FormControl(value));

  it('passes a real address', () => {
    expect(check('jane@example.co.za')).toBeNull();
  });

  it('passes an empty box, so the field can be optional', () => {
    expect(check('')).toBeNull();
    expect(check(null)).toBeNull();
  });

  it('refuses spaces around the address, as the server does', () => {
    expect(check(' jane@example.co.za ')).toEqual({ email: true });
  });

  it('fails anything else with the error key Angular itself uses for email', () => {
    expect(check('jane@example')).toEqual({ email: true });
  });
});
