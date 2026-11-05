import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { IDLE_TIMINGS, IdleService } from './idle.service';

const MINUTE = 60_000;
const OTHER_TAB_KEY = 'cleared.last-activity';

describe('IdleService', () => {
  let service: IdleService;
  let router: Router;
  let refreshSession: ReturnType<typeof vi.fn>;
  let signOutQuietly: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-05T09:00:00Z'));
    refreshSession = vi.fn(() => Promise.resolve('token'));
    signOutQuietly = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { refreshSession, signOutQuietly } },
        {
          provide: IDLE_TIMINGS,
          useValue: {
            warnAfterMs: 15 * MINUTE,
            signOutAfterMs: 20 * MINUTE,
            heartbeatMs: 10 * MINUTE,
          },
        },
      ],
    });

    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    service = TestBed.inject(IdleService);
    service.start();
  });

  afterEach(() => {
    service.stop();
    vi.useRealTimers();
    localStorage.clear();
  });

  const pass = (ms: number) => vi.advanceTimersByTime(ms);
  const happen = (name = 'pointermove') => document.dispatchEvent(new Event(name));

  it('says nothing while the person is active', () => {
    pass(14 * MINUTE + 59_000);

    expect(service.warning()).toBe(false);
    expect(signOutQuietly).not.toHaveBeenCalled();
  });

  it('warns after fifteen quiet minutes, with five minutes left, and counts down', () => {
    pass(15 * MINUTE);

    expect(service.warning()).toBe(true);
    expect(service.secondsLeft()).toBe(300);

    pass(60_000);

    expect(service.secondsLeft()).toBe(240);
  });

  it('signs the person out after twenty quiet minutes, once, giving inactivity as the reason', () => {
    pass(20 * MINUTE - 1000);

    expect(signOutQuietly).not.toHaveBeenCalled();

    pass(1000);

    expect(signOutQuietly).toHaveBeenCalledWith('idle');

    pass(5 * MINUTE);

    expect(signOutQuietly).toHaveBeenCalledTimes(1);
  });

  it.each(['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'scroll'])(
    'starts counting again after a %s',
    (name) => {
      pass(14 * MINUTE);
      happen(name);
      pass(14 * MINUTE);

      expect(service.warning()).toBe(false);
      expect(signOutQuietly).not.toHaveBeenCalled();
    },
  );

  it('sees scrolling inside a panel, which does not bubble up to the page', () => {
    const panel = document.createElement('div');
    document.body.appendChild(panel);

    pass(14 * MINUTE);
    panel.dispatchEvent(new Event('scroll'));
    pass(14 * MINUTE);
    panel.remove();

    expect(service.warning()).toBe(false);
  });

  it('goes by activity in another tab, which it finds in the shared record', () => {
    pass(14 * MINUTE);
    localStorage.setItem(OTHER_TAB_KEY, String(Date.now()));
    pass(14 * MINUTE);

    expect(service.warning()).toBe(false);
    expect(signOutQuietly).not.toHaveBeenCalled();
  });

  it('writes its own activity where the other tabs can read it', () => {
    pass(5 * MINUTE);
    happen();

    expect(localStorage.getItem(OTHER_TAB_KEY)).toBe(String(Date.now()));
  });

  it('does not let a nudge of the mouse dismiss the warning', () => {
    pass(15 * MINUTE);

    happen();
    pass(1000);

    expect(service.warning()).toBe(true);
    expect(service.secondsLeft()).toBe(299);
  });

  it('stays signed in, renewing the session at once and starting the count again', () => {
    pass(16 * MINUTE);

    service.staySignedIn();

    expect(refreshSession).toHaveBeenCalledTimes(1);
    expect(service.warning()).toBe(false);

    pass(14 * MINUTE);

    expect(service.warning()).toBe(false);

    pass(MINUTE);

    expect(service.warning()).toBe(true);
  });

  it('signs out at once, with no warning, after a computer that slept', () => {
    vi.setSystemTime(Date.now() + 3 * 60 * MINUTE);
    pass(1000);

    expect(signOutQuietly).toHaveBeenCalledWith('idle');
    expect(service.warning()).toBe(false);
  });

  it('checks as soon as the page comes back, without waiting for a throttled timer', () => {
    vi.setSystemTime(Date.now() + 16 * MINUTE);

    document.dispatchEvent(new Event('visibilitychange'));

    expect(service.warning()).toBe(true);
  });

  it('renews the session about every ten minutes for someone who is using the app', () => {
    pass(5 * MINUTE);
    happen();

    expect(refreshSession).not.toHaveBeenCalled();

    pass(5 * MINUTE);
    happen();
    pass(2000);
    happen();

    expect(refreshSession).toHaveBeenCalledTimes(1);
  });

  it('does not renew a session that nobody is using, so the server can let it lapse', () => {
    pass(14 * MINUTE);

    expect(refreshSession).not.toHaveBeenCalled();
  });

  it('goes to sign-in when the renewal finds the session already over', async () => {
    refreshSession.mockResolvedValue(null);

    pass(10 * MINUTE);
    happen();
    await vi.advanceTimersByTimeAsync(0);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('tries again later when a renewal could not be made, and stays where it is', async () => {
    refreshSession.mockRejectedValueOnce(new Error('offline'));

    pass(10 * MINUTE);
    happen();
    await vi.advanceTimersByTimeAsync(0);
    pass(10 * MINUTE);
    happen();

    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(refreshSession).toHaveBeenCalledTimes(2);
  });

  it('stops watching when it is stopped', () => {
    service.stop();

    pass(25 * MINUTE);
    happen();

    expect(signOutQuietly).not.toHaveBeenCalled();
    expect(refreshSession).not.toHaveBeenCalled();
  });

  it('is not watching twice when it is started twice', () => {
    service.start();

    pass(20 * MINUTE);

    expect(signOutQuietly).toHaveBeenCalledTimes(1);
  });

  it('signs out on request, with no reason given, and stops watching', () => {
    service.signOutNow();
    pass(25 * MINUTE);

    expect(signOutQuietly).toHaveBeenCalledTimes(1);
    expect(signOutQuietly).toHaveBeenCalledWith();
  });
});
