import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { InvoiceService } from '@core/services/invoice.service';
import { Invoice } from '@core/models/invoice.model';
import { StatusBadge } from '@shared/components/status-badge/status-badge';

@Component({
  selector: 'app-invoice-detail',
  imports: [ReactiveFormsModule, RouterLink, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-detail.html',
})
export class InvoiceDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly invoiceService = inject(InvoiceService);
  private readonly fb = inject(FormBuilder);

  protected readonly invoice = signal<Invoice | null>(null);
  protected readonly loading = signal(true);
  protected readonly issuing = signal(false);
  protected readonly showIssueForm = signal(false);

  protected readonly issueForm = this.fb.nonNullable.group({
    issueDate: [today(), Validators.required],
    dueDate: [in30Days(), Validators.required],
  });

  constructor() {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.load(id);
  }

  private load(id: string): void {
    this.loading.set(true);
    this.invoiceService.getById(id).subscribe({
      next: (invoice) => {
        this.invoice.set(invoice);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  protected confirmIssue(): void {
    const current = this.invoice();
    if (!current || this.issueForm.invalid) {
      return;
    }

    this.issuing.set(true);

    this.invoiceService.issue(current.id, this.issueForm.getRawValue()).subscribe({
      next: (updated) => {
        this.invoice.set(updated);
        this.issuing.set(false);
        this.showIssueForm.set(false);
      },
      error: () => this.issuing.set(false),
    });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function in30Days(): string {
  const date = new Date();
  date.setDate(date.getDate() + 30);
  return date.toISOString().slice(0, 10);
}
