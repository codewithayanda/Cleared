import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { InvoiceStatus } from '@core/models/invoice.model';

const STATUS_STYLES: Record<InvoiceStatus, string> = {
  Draft: 'bg-steel-100 text-steel-600',
  Issued: 'bg-blue-50 text-status-issued',
  PartiallyPaid: 'bg-gold-100 text-gold-700',
  Paid: 'bg-green-50 text-status-paid',
  Cancelled: 'bg-steel-100 text-status-cancelled line-through',
};

@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium"
      [class]="styles()"
    >
      {{ label() }}
    </span>
  `,
})
export class StatusBadge {
  readonly status = input.required<InvoiceStatus>();

  protected readonly styles = computed(() => STATUS_STYLES[this.status()]);

  protected readonly label = computed(() => {
    // "PartiallyPaid" -> "Partially Paid"
    return this.status().replace(/([a-z])([A-Z])/g, '$1 $2');
  });
}
