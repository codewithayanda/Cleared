import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CustomerService } from '@core/services/customer.service';
import { InvoiceService } from '@core/services/invoice.service';
import { Customer } from '@core/models/customer.model';
import { VatTreatment } from '@core/models/invoice.model';

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

  protected readonly customers = signal<Customer[]>([]);
  protected readonly submitting = signal(false);

  // NotApplicable isn't offered here — see VatTreatment.
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
  }

  protected get lines() {
    return this.form.controls.lines;
  }

  protected newLine() {
    return this.fb.nonNullable.group({
      description: ['', Validators.required],
      quantity: [1, [Validators.required, Validators.min(0.0001)]],
      unitPrice: [0, [Validators.required, Validators.min(0.01)]],
      vatTreatment: this.fb.nonNullable.control<VatTreatment>('Standard'),
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

  // Display-only preview — quantity × price, nothing tax-related to get wrong yet. The
  // server recomputes and stores the authoritative figure; this is purely for the person
  // filling in the form to see a running total as they type.
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

    this.submitting.set(true);

    this.invoiceService
      .create({
        customerId,
        lines: lines.map((line) => ({
          description: line.description,
          quantity: line.quantity,
          unitPrice: line.unitPrice.toFixed(2),
          vatTreatment: line.vatTreatment,
        })),
      })
      .subscribe({
        next: (invoice) => this.router.navigate(['/invoices', invoice.id]),
        error: () => this.submitting.set(false),
      });
  }
}
