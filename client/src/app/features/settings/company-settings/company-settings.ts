import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TenantService } from '@core/services/tenant.service';

@Component({
  selector: 'app-company-settings',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './company-settings.html',
})
export class CompanySettings {
  private readonly fb = inject(FormBuilder);
  private readonly tenantService = inject(TenantService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);

  protected readonly companyName = signal('');
  protected readonly vatStatus = signal('');
  protected readonly vatNumber = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    address: [''],
    bankName: [''],
    bankAccountNumber: [''],
    bankBranchCode: [''],
  });

  constructor() {
    this.tenantService.getMine().subscribe({
      next: (tenant) => {
        this.companyName.set(tenant.companyName);
        this.vatStatus.set(tenant.vatStatus);
        this.vatNumber.set(tenant.vatNumber);
        this.form.patchValue({
          address: tenant.address ?? '',
          bankName: tenant.bankName ?? '',
          bankAccountNumber: tenant.bankAccountNumber ?? '',
          bankBranchCode: tenant.bankBranchCode ?? '',
        });
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    const { address, bankName, bankAccountNumber, bankBranchCode } = this.form.getRawValue();

    this.saving.set(true);
    this.saved.set(false);

    this.tenantService
      .updateProfile({
        address: address || null,
        bankName: bankName || null,
        bankAccountNumber: bankAccountNumber || null,
        bankBranchCode: bankBranchCode || null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.saved.set(true);
        },
        error: () => this.saving.set(false),
      });
  }
}
