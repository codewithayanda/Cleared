import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { IDLE_TIMINGS, IdleService } from '@core/services/idle.service';
import { IdleWarning } from './idle-warning';

describe('IdleWarning', () => {
  let warning: WritableSignal<boolean>;
  let secondsLeft: WritableSignal<number>;
  let staySignedIn: ReturnType<typeof vi.fn>;
  let signOutNow: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    warning = signal(false);
    secondsLeft = signal(300);
    staySignedIn = vi.fn();
    signOutNow = vi.fn();

    TestBed.configureTestingModule({
      imports: [IdleWarning],
      providers: [
        { provide: IdleService, useValue: { warning, secondsLeft, staySignedIn, signOutNow } },
        {
          provide: IDLE_TIMINGS,
          useValue: { warnAfterMs: 15 * 60_000, signOutAfterMs: 20 * 60_000, heartbeatMs: 600_000 },
        },
      ],
    });
  });

  const added: HTMLElement[] = [];

  afterEach(() => added.splice(0).forEach((element) => element.remove()));

  function addButtonToThePage(): HTMLButtonElement {
    const button = document.createElement('button');
    document.body.appendChild(button);
    added.push(button);

    return button;
  }

  async function render() {
    const fixture = TestBed.createComponent(IdleWarning);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
    };

    return {
      settle,
      dialog: () => element.querySelector<HTMLElement>('[role="alertdialog"]'),
      buttons: () => [...element.querySelectorAll<HTMLButtonElement>('button')],
      text: (selector: string) => element.querySelector(selector)?.textContent?.trim(),
      press: (key: string, shiftKey = false) => {
        const event = new KeyboardEvent('keydown', {
          key,
          shiftKey,
          bubbles: true,
          cancelable: true,
        });
        (document.activeElement ?? document.body).dispatchEvent(event);

        return event;
      },
    };
  }

  it('shows nothing while the person is active', async () => {
    const { dialog } = await render();

    expect(dialog()).toBeNull();
  });

  it('is an alert dialog that blocks the page, named and described for a screen reader', async () => {
    warning.set(true);
    const { settle, dialog, text } = await render();
    await settle();

    const element = dialog()!;
    const title = document.getElementById(element.getAttribute('aria-labelledby')!);
    const description = document.getElementById(element.getAttribute('aria-describedby')!);

    expect(element.getAttribute('aria-modal')).toBe('true');
    expect(title?.textContent).toContain('Are you still there?');
    expect(description?.textContent).toContain('15 minutes');
    expect(description?.textContent).toContain('5 minutes');
    expect(text('button')).toBe('Stay signed in');
  });

  it('shows the time left as minutes and seconds, and hides that from a screen reader', async () => {
    warning.set(true);
    secondsLeft.set(272);
    const { settle, dialog } = await render();
    await settle();

    const visible = dialog()!.querySelector('[aria-hidden="true"]');

    expect(visible?.textContent?.trim()).toBe('4:32');
  });

  it('speaks the time left in steps, so it is not read out every second', async () => {
    warning.set(true);
    const { settle, text } = await render();
    await settle();

    expect(text('[role="status"]')).toBe('Signing out in 5 minutes.');

    secondsLeft.set(299);
    await settle();

    expect(text('[role="status"]')).toBe('Signing out in 5 minutes.');

    secondsLeft.set(240);
    await settle();

    expect(text('[role="status"]')).toBe('Signing out in 4 minutes.');

    secondsLeft.set(25);
    await settle();

    expect(text('[role="status"]')).toBe('Signing out in 30 seconds.');
  });

  it('puts the focus on Stay signed in when it opens', async () => {
    const { settle, buttons } = await render();

    warning.set(true);
    await settle();

    expect(document.activeElement).toBe(buttons()[0]);
  });

  it('gives the focus back to where it was when the warning goes away', async () => {
    const before = addButtonToThePage();
    before.focus();
    const { settle } = await render();

    warning.set(true);
    await settle();
    warning.set(false);
    await settle();

    expect(document.activeElement).toBe(before);
  });

  it('keeps Tab and Shift+Tab inside the dialog', async () => {
    const { settle, buttons, press } = await render();
    warning.set(true);
    await settle();
    const [stay, signOut] = buttons();

    signOut.focus();
    const forward = press('Tab');

    expect(forward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(stay);

    const backward = press('Tab', true);

    expect(backward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(signOut);
  });

  it('brings the focus back in if it has somehow left the dialog', async () => {
    const outside = addButtonToThePage();
    const { settle, buttons, press } = await render();
    warning.set(true);
    await settle();

    outside.focus();
    press('Tab');

    expect(document.activeElement).toBe(buttons()[0]);
  });

  it('leaves the keyboard alone while there is no warning, though it listens on the whole page', async () => {
    const { press } = await render();

    const event = press('Escape');

    expect(event.defaultPrevented).toBe(false);
    expect(staySignedIn).not.toHaveBeenCalled();
  });

  it('lets Escape mean stay signed in', async () => {
    const { settle, press } = await render();
    warning.set(true);
    await settle();

    const event = press('Escape');

    expect(event.defaultPrevented).toBe(true);
    expect(staySignedIn).toHaveBeenCalledTimes(1);
  });

  it('stays signed in, or signs out, as the buttons say', async () => {
    const { settle, buttons } = await render();
    warning.set(true);
    await settle();
    const [stay, signOut] = buttons();

    stay.click();

    expect(staySignedIn).toHaveBeenCalledTimes(1);
    expect(signOutNow).not.toHaveBeenCalled();

    signOut.click();

    expect(signOutNow).toHaveBeenCalledTimes(1);
  });
});
