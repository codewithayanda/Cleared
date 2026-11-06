import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CustomerService } from '@core/services/customer.service';
import { InvoiceService } from '@core/services/invoice.service';
import { Customer } from '@core/models/customer.model';
import { Invoice, InvoiceLine, VatTreatment } from '@core/models/invoice.model';
import { IdempotencyKey } from '@core/utils/idempotency-key';

@Component({
  selector: 'app-invoice-form',
  imports: [ReactiveFormsModule, RouterLink, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-form.html',
})
export class InvoiceForm {
  private readonly fb = inject(FormBuilder);
  private readonly customerService = inject(CustomerService);
  private readonly invoiceService = inject(InvoiceService);
  private readonly router = inject(Router);

  // The draft being changed, or null when the page is for starting a new one.
  private readonly editingId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  // Reused by every retry of this form. The page is left on success, so it is never renewed.
  private readonly idempotencyKey = new IdempotencyKey();

  protected readonly editing = this.editingId !== null;
  protected readonly loading = signal(this.editing);
  protected readonly notFound = signal(false);
  protected readonly issued = signal(false);
  protected readonly customers = signal<Customer[]>([]);
  protected readonly submitting = signal(false);
  protected readonly saveError = signal<string | null>(null);

  // Leaving goes back to where the person came from: the draft when it is being changed, else the list.
  protected readonly leaveLink =
    this.editingId === null ? ['/invoices'] : ['/invoices', this.editingId];

  protected readonly submitLabel = computed(() => {
    if (this.submitting()) {
      return 'Saving…';
    }

    return this.editing ? 'Save changes' : 'Save draft';
  });

  // NotApplicable isn't offered here. See VatTreatment.
  protected readonly vatTreatments: { value: VatTreatment; label: string }[] = [
    { value: 'Standard', label: 'Standard' },
    { value: 'ZeroRated', label: 'Zero-rated' },
    { value: 'Exempt', label: 'Exempt' },
  ];

  protected readonly form = this.fb.nonNullable.group({
    customerId: ['', Validators.required],
    lines: this.fb.nonNullable.array([this.newLine()]),
  });

  constructor() {
    this.customerService.list().subscribe((customers) => this.customers.set(customers));

    if (this.editingId === null) {
      return;
    }

    this.invoiceService.getById(this.editingId).subscribe({
      next: (invoice) => {
        // Only a draft can be changed. The server refuses the rest, so say so before anything is typed.
        if (invoice.status === 'Draft') {
          this.fill(invoice);
        } else {
          this.issued.set(true);
        }
        this.loading.set(false);
      },
      error: () => {
        this.notFound.set(true);
        this.loading.set(false);
      },
    });
  }

  protected get lines() {
    return this.form.controls.lines;
  }

  // The form offers three treatments. A line saved as NotApplicable, which is what a business that is not
  // VAT registered always gets, opens as Standard, and the server makes it NotApplicable again for such a
  // business when the change is saved.
  protected newLine(line?: InvoiceLine) {
    return this.fb.nonNullable.group({
      description: [line?.description ?? '', Validators.required],
      quantity: [line?.quantity ?? 1, [Validators.required, Validators.min(0.0001)]],
      unitPrice: [line ? Number(line.unitPrice) : 0, [Validators.required, Validators.min(0.01)]],
      vatTreatment: this.fb.nonNullable.control<VatTreatment>(
        !line || line.vatTreatment === 'NotApplicable' ? 'Standard' : line.vatTreatment,
      ),
    });
  }

  protected addLine(): void {
    this.lines.push(this.newLine());
  }

  protected removeLine(index: number): void {
    if (this.lines.length > 1) {
      this.lines.removeAt(index);
    }
  }

  // Display-only preview: quantity times price, nothing tax-related yet. The server
  // recomputes the authoritative figure; this just shows a running total as you type.
  protected lineTotal(index: number): number {
    const line = this.lines.at(index).value;
    return (line.quantity ?? 0) * (line.unitPrice ?? 0);
  }

  protected get subtotal(): number {
    return this.lines.controls.reduce((sum, _, index) => sum + this.lineTotal(index), 0);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { customerId, lines } = this.form.getRawValue();
    const request = {
      customerId,
      lines: lines.map((line) => ({
        description: line.description,
        quantity: line.quantity,
        unitPrice: line.unitPrice.toFixed(2),
        vatTreatment: line.vatTreatment,
      })),
    };

    this.submitting.set(true);
    this.saveError.set(null);

    const save =
      this.editingId === null
        ? this.invoiceService.create(request, this.idempotencyKey.value)
        : this.invoiceService.update(this.editingId, request);

    save.subscribe({
      next: (invoice) => this.router.navigate(['/invoices', invoice.id]),
      error: (error: unknown) => {
        this.submitting.set(false);
        this.saveError.set(saveErrorMessage(error));
      },
    });
  }

  private fill(invoice: Invoice): void {
    this.lines.clear();
    invoice.lines.forEach((line) => this.lines.push(this.newLine(line)));

    // A draft with no lines still gets one empty line to type into.
    if (this.lines.length === 0) {
      this.addLine();
    }

    this.form.controls.customerId.setValue(invoice.customerId);
  }
}

// The two answers the server gives that the person can act on, and a general one for the rest.
function saveErrorMessage(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : 0;

  if (status === 409) {
    return 'This invoice has been issued, so it can no longer be changed.';
  }

  if (status === 404) {
    return 'This draft no longer exists.';
  }

  return 'Could not save the invoice. Please try again.';
}
