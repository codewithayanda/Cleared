import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CustomerService } from '@core/services/customer.service';
import { IdempotencyKey } from '@core/utils/idempotency-key';

@Component({
  selector: 'app-customer-form',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customer-form.html',
})
export class CustomerForm {
  private readonly fb = inject(FormBuilder);
  private readonly customerService = inject(CustomerService);
  private readonly router = inject(Router);

  // Reused by every retry of this form. The page is left on success, so it is never renewed.
  private readonly idempotencyKey = new IdempotencyKey();

  protected readonly submitting = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    email: [''],
    vatNumber: [''],
    address: [''],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, email, vatNumber, address } = this.form.getRawValue();

    this.submitting.set(true);

    this.customerService
      .create(
        {
          name,
          email: email || null,
          vatNumber: vatNumber || null,
          address: address || null,
        },
        this.idempotencyKey.value,
      )
      .subscribe({
        next: () => this.router.navigateByUrl('/customers'),
        error: () => this.submitting.set(false),
      });
  }
}
