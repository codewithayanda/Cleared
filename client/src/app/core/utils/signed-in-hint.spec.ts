import { signedInHint } from './signed-in-hint';

describe('signedInHint', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('is not set until someone signs in', () => {
    expect(signedInHint.isSet()).toBe(false);
  });

  it('is set after set() and gone after clear()', () => {
    signedInHint.set();
    expect(signedInHint.isSet()).toBe(true);

    signedInHint.clear();

    expect(signedInHint.isSet()).toBe(false);
  });

  it('assumes someone may be signed in when storage cannot be read', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(signedInHint.isSet()).toBe(true);
  });

  it('does not throw when storage cannot be written', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(() => signedInHint.set()).not.toThrow();
    expect(() => signedInHint.clear()).not.toThrow();
  });
});
