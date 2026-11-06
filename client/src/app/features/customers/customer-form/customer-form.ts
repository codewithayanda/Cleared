import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CustomerService } from '@core/services/customer.service';
import { validEmail } from '@core/utils/email-rule';
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

  // The customer being changed, or null when the page is for adding one.
  private readonly editingId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  // Reused by every retry of this form. The page is left on success, so it is never renewed.
  private readonly idempotencyKey = new IdempotencyKey();

  protected readonly editing = this.editingId !== null;
  protected readonly loading = signal(this.editing);
  protected readonly notFound = signal(false);
  protected readonly submitting = signal(false);
  protected readonly saveFailed = signal(false);

  protected readonly submitLabel = computed(() => {
    if (this.submitting()) {
      return 'Saving…';
    }

    return this.editing ? 'Save changes' : 'Save customer';
  });

  protected readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    email: ['', validEmail],
    vatNumber: [''],
    address: [''],
  });

  constructor() {
    if (this.editingId === null) {
      return;
    }

    this.customerService.getById(this.editingId).subscribe({
      next: (customer) => {
        this.form.setValue({
          name: customer.name,
          email: customer.email ?? '',
          vatNumber: customer.vatNumber ?? '',
          address: customer.address ?? '',
        });
        this.loading.set(false);
      },
      error: () => {
        this.notFound.set(true);
        this.loading.set(false);
      },
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, email, vatNumber, address } = this.form.getRawValue();
    const request = {
      name,
      email: email || null,
      vatNumber: vatNumber || null,
      address: address || null,
    };

    this.submitting.set(true);
    this.saveFailed.set(false);

    const save =
      this.editingId === null
        ? this.customerService.create(request, this.idempotencyKey.value)
        : this.customerService.update(this.editingId, request);

    save.subscribe({
      next: () => this.router.navigateByUrl('/customers'),
      error: () => {
        this.submitting.set(false);
        this.saveFailed.set(true);
      },
    });
  }
}
