import { TestBed } from '@angular/core/testing';
import { LockoutTracker } from './lockout-tracker';

const MINUTE = 60_000;

describe('LockoutTracker', () => {
  let tracker: LockoutTracker;

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T10:00:00Z'));
    tracker = TestBed.inject(LockoutTracker);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  function failTimes(count: number, email = 'a@b.co.za'): number | null {
    let result: number | null = null;

    for (let attempt = 0; attempt < count; attempt++) {
      result = tracker.recordFailure(email);
    }

    return result;
  }

  it('is not locked until the fifth wrong password', () => {
    expect(failTimes(4)).toBeNull();
    expect(tracker.lockedUntil('a@b.co.za')).toBeNull();

    const until = tracker.recordFailure('a@b.co.za');

    expect(until).toBe(Date.now() + 15 * MINUTE);
    expect(tracker.lockedUntil('a@b.co.za')).toBe(until);
  });

  it('keeps the lock when the page is loaded again', () => {
    failTimes(5);

    TestBed.resetTestingModule();
    const afterReload = TestBed.inject(LockoutTracker);

    expect(afterReload.lockedUntil('a@b.co.za')).toBe(Date.now() + 15 * MINUTE);
  });

  it('counts failures across reloads too, as the server does', () => {
    failTimes(3);

    TestBed.resetTestingModule();
    const afterReload = TestBed.inject(LockoutTracker);

    expect(afterReload.recordFailure('a@b.co.za')).toBeNull();
    expect(afterReload.recordFailure('a@b.co.za')).not.toBeNull();
  });

  it('lets the lock go after 15 minutes and starts counting again from nothing', () => {
    failTimes(5);

    vi.advanceTimersByTime(15 * MINUTE + 1);

    expect(tracker.lockedUntil('a@b.co.za')).toBeNull();
    expect(failTimes(4)).toBeNull();
  });

  it('does not push the end of a lock further away', () => {
    const until = failTimes(5);

    vi.advanceTimersByTime(5 * MINUTE);

    expect(tracker.recordFailure('a@b.co.za')).toBe(until);
  });

  it('counts each email on its own, ignoring case and spaces', () => {
    failTimes(5, 'First@B.co.za ');

    expect(tracker.lockedUntil('first@b.co.za')).not.toBeNull();
    expect(tracker.lockedUntil('second@b.co.za')).toBeNull();
  });

  it('forgets everything after a good sign-in', () => {
    failTimes(4);

    tracker.recordSuccess('a@b.co.za');

    expect(failTimes(4)).toBeNull();
  });

  it('forgets counts nobody added to for a day', () => {
    failTimes(4);

    vi.advanceTimersByTime(25 * 60 * MINUTE);

    expect(failTimes(4)).toBeNull();
  });

  it('still works from memory when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    failTimes(5);

    expect(tracker.lockedUntil('a@b.co.za')).toBe(Date.now() + 15 * MINUTE);
  });
});
