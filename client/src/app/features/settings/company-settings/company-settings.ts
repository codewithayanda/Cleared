import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { AccountService } from '@core/services/account.service';
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

  protected readonly accounts = inject(AccountService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

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
    this.errorMessage.set(null);

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
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(
            emailNotConfirmed(error)
              ? 'Confirm your email address first. Banking details can be changed only after that.'
              : 'Could not save. Please try again.',
          );
        },
      });
  }
}

// The API refuses a change to banking details with this code until the email has been confirmed.
function emailNotConfirmed(error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 403 &&
    (error.error as { code?: string } | null)?.code === 'email_not_verified'
  );
}
