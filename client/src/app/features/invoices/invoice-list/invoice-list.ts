import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InvoiceService } from '@core/services/invoice.service';
import { Invoice } from '@core/models/invoice.model';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { StatusBadge } from '@shared/components/status-badge/status-badge';

@Component({
  selector: 'app-invoice-list',
  imports: [RouterLink, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-list.html',
})
export class InvoiceList {
  private readonly invoiceService = inject(InvoiceService);

  protected readonly invoices = signal<Invoice[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.invoiceService.list().subscribe({
      next: (invoices) => {
        this.invoices.set(invoices);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
