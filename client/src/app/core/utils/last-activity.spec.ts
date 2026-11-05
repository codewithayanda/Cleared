import { lastActivity } from './last-activity';

const KEY = 'cleared.last-activity';

describe('lastActivity', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('has no answer before anyone has been active', () => {
    expect(lastActivity.read()).toBeNull();
  });

  it('gives back the time that was written, for any tab to read', () => {
    lastActivity.write(1_800_000_000_000);

    expect(lastActivity.read()).toBe(1_800_000_000_000);
    expect(localStorage.getItem(KEY)).toBe('1800000000000');
  });

  it('has no answer for something that is not a time', () => {
    localStorage.setItem(KEY, 'later');

    expect(lastActivity.read()).toBeNull();
  });

  it('has no answer, and does not fail, when storage cannot be used', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(() => lastActivity.write(5)).not.toThrow();
    expect(lastActivity.read()).toBeNull();
  });
});
