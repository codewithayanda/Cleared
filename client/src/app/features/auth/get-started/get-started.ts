import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { AuthService } from '@core/services/auth.service';
import { VatStatus } from '@core/models/auth.model';

@Component({
  selector: 'app-get-started',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './get-started.html',
})
export class GetStarted {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    companyName: ['', Validators.required],
    vatStatus: ['NotRegistered' as VatStatus, Validators.required],
    vatNumber: [''],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  protected get isVatRegistered(): boolean {
    return this.form.controls.vatStatus.value === 'Registered';
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { companyName, vatStatus, vatNumber, email, password } = this.form.getRawValue();

    this.submitting.set(true);
    this.errorMessage.set(null);

    this.auth
      .registerTenant({
        companyName,
        vatStatus,
        vatNumber: vatStatus === 'Registered' ? vatNumber || null : null,
        tradingName: null,
      })
      .pipe(switchMap((tenant) => this.auth.register({ tenantId: tenant.id, email, password })))
      .subscribe({
        next: () => this.router.navigateByUrl('/dashboard'),
        error: () => {
          this.submitting.set(false);
          this.errorMessage.set('Could not create your account. Check your details and try again.');
        },
      });
  }
}
