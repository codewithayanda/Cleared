import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { IDLE_TIMINGS, IdleService } from '@core/services/idle.service';
import { spokenTime } from '@core/utils/spoken-time';

@Component({
  selector: 'app-idle-warning',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './idle-warning.html',
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class IdleWarning {
  protected readonly idle = inject(IdleService);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild<ElementRef<HTMLElement>>('dialog');
  private readonly stayButton = viewChild<ElementRef<HTMLButtonElement>>('stayAction');
  private opener: HTMLElement | null = null;

  protected readonly warnMinutes = inject(IDLE_TIMINGS).warnAfterMs / 60_000;

  protected readonly countdown = computed(() => {
    const seconds = this.idle.secondsLeft();

    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  protected readonly spoken = computed(() => spokenTime(this.idle.secondsLeft()));

  // Focus goes to the safe choice when the dialog opens, and back to where it was when it closes.
  constructor() {
    effect(() => {
      if (this.idle.warning()) {
        this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        afterNextRender(() => this.stayButton()?.nativeElement.focus(), {
          injector: this.injector,
        });
      } else {
        this.opener?.focus();
        this.opener = null;
      }
    });
  }

  protected stay(): void {
    this.idle.staySignedIn();
  }

  protected signOut(): void {
    this.idle.signOutNow();
  }

  // Heard on the whole page, so a key pressed while focus is somewhere behind the dialog is caught too.
  // It keeps the keyboard inside the dialog and lets Escape mean "stay signed in".
  protected onKeydown(event: KeyboardEvent): void {
    const root = this.dialog()?.nativeElement;

    if (!this.idle.warning() || !root) {
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      this.stay();

      return;
    }

    const buttons = root.querySelectorAll('button');

    if (event.key !== 'Tab' || !buttons.length) {
      return;
    }

    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    const outside = !root.contains(document.activeElement);

    if (outside || (event.shiftKey && document.activeElement === first)) {
      event.preventDefault();
      (outside ? first : last).focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
}
