import { installFakeLocks, removeFakeLocks } from '@core/testing/fake-locks';
import { withTabLock } from './tab-lock';

describe('withTabLock', () => {
  afterEach(() => removeFakeLocks());

  it('runs the work inside the lock when the browser has Web Locks', async () => {
    const locks = installFakeLocks();

    expect(await withTabLock('refresh', () => Promise.resolve(42))).toBe(42);
    expect(locks.requested).toEqual(['refresh']);
  });

  it('runs the work straight away when the browser has none', async () => {
    expect(await withTabLock('refresh', () => Promise.resolve(7))).toBe(7);
  });

  it('makes callers take turns', async () => {
    installFakeLocks();
    const order: string[] = [];
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });

    const first = withTabLock('refresh', async () => {
      order.push('first starts');
      await held;
      order.push('first ends');
    });
    const second = withTabLock('refresh', async () => {
      order.push('second starts');
    });

    await vi.waitFor(() => expect(order).toEqual(['first starts']));
    release();
    await Promise.all([first, second]);

    expect(order).toEqual(['first starts', 'first ends', 'second starts']);
  });

  it('lets the next caller in when the work fails', async () => {
    installFakeLocks();

    await expect(withTabLock('refresh', () => Promise.reject(new Error('boom')))).rejects.toThrow(
      'boom',
    );

    expect(await withTabLock('refresh', () => Promise.resolve('ok'))).toBe('ok');
  });
});
