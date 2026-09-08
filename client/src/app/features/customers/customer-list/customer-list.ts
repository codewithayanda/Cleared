import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CustomerService } from '@core/services/customer.service';
import { Customer } from '@core/models/customer.model';
import { EmptyState } from '@shared/components/empty-state/empty-state';

@Component({
  selector: 'app-customer-list',
  imports: [RouterLink, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customer-list.html',
})
export class CustomerList {
  private readonly customerService = inject(CustomerService);

  protected readonly customers = signal<Customer[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.customerService.list().subscribe({
      next: (customers) => {
        this.customers.set(customers);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
