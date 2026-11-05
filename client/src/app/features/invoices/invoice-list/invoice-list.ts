import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InvoiceService } from '@core/services/invoice.service';
import { Invoice } from '@core/models/invoice.model';
import { EmptyState } from '@shared/components/empty-state/empty-state';
import { StatusBadge } from '@shared/components/status-badge/status-badge';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

@Component({
  selector: 'app-invoice-list',
  imports: [RouterLink, EmptyState, StatusBadge, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-list.html',
})
export class InvoiceList {
  protected readonly searchByNumber = new FormControl('');
  protected readonly overdueOnly = new FormControl(false, { nonNullable: true });
  private readonly invoiceService = inject(InvoiceService);

  protected readonly invoices = signal<Invoice[]>([]);
  protected readonly loading = signal(true);
  protected readonly overdueCount = computed(
    () => this.invoices().filter((invoice) => invoice.isOverdue).length,
  );

  constructor() {
    this.invoiceService.list().subscribe({
      next: (invoices) => {
        this.invoices.set(invoices);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  filterInvoices(invoices: Invoice[], searchTerm: string | null, overdueOnly = false) {
    const wanted = overdueOnly ? invoices.filter((invoice) => invoice.isOverdue) : invoices;

    if (searchTerm) {
      return wanted.filter((invoice) =>
        invoice.number?.toLowerCase().includes(searchTerm.toLowerCase()),
      );
    }
    return wanted;
  }

  // Says why the table is empty, so an empty filter is not mistaken for no invoices at all.
  protected emptyMessage(overdueOnly: boolean, searchTerm: string | null): string {
    if (overdueOnly) {
      return searchTerm ? 'No overdue invoices match your search.' : 'No overdue invoices.';
    }
    return 'No invoices match your search.';
  }
}
