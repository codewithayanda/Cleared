import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { InvoiceService } from '@core/services/invoice.service';
import { PaymentService } from '@core/services/payment.service';
import { Invoice, ProblemDetails, VatTreatment } from '@core/models/invoice.model';
import { StatusBadge } from '@shared/components/status-badge/status-badge';

const VAT_TREATMENT_LABELS: Record<VatTreatment, string> = {
  Standard: 'Standard',
  ZeroRated: 'Zero-rated',
  Exempt: 'Exempt',
  NotApplicable: 'N/A',
};

@Component({
  selector: 'app-invoice-detail',
  imports: [ReactiveFormsModule, RouterLink, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invoice-detail.html',
})
export class InvoiceDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly invoiceService = inject(InvoiceService);
  private readonly paymentService = inject(PaymentService);
  private readonly fb = inject(FormBuilder);

  protected readonly invoice = signal<Invoice | null>(null);
  protected readonly loading = signal(true);
  protected readonly issuing = signal(false);
  protected readonly showIssueForm = signal(false);
  protected readonly issueError = signal<string | null>(null);
  protected readonly issueErrorFields = signal<string[] | null>(null);

  protected readonly showPaymentForm = signal(false);
  protected readonly recordingPayment = signal(false);
  protected readonly paymentError = signal<string | null>(null);

  protected readonly downloadingPdf = signal(false);

  // Can a payment be recorded at all? Mirrors Invoice.RecordPaymentTotal's own guard —
  // an invoice that's Draft, already Paid, or Cancelled can't take one.
  protected readonly canRecordPayment = computed(() => {
    const status = this.invoice()?.status;
    return status === 'Issued' || status === 'PartiallyPaid';
  });

  // "0.150000" -> "15" — a rate is a fraction snapshotted at issue (see VatRate), never
  // recomputed; this only formats it for display.
  protected readonly vatRatePercentage = computed(() => {
    const rate = this.invoice()?.vatRateApplied;
    return rate ? (Number(rate) * 100).toFixed(0) : null;
  });

  protected readonly issueForm = this.fb.nonNullable.group({
    issueDate: [today(), Validators.required],
    dueDate: [in30Days(), Validators.required],
  });

  protected readonly paymentForm = this.fb.nonNullable.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    receivedAt: [today(), Validators.required],
    reference: [''],
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

  protected treatmentLabel(treatment: VatTreatment): string {
    return VAT_TREATMENT_LABELS[treatment];
  }

  protected confirmIssue(): void {
    const current = this.invoice();
    if (!current || this.issueForm.invalid) {
      return;
    }

    this.issuing.set(true);
    this.issueError.set(null);
    this.issueErrorFields.set(null);

    this.invoiceService.issue(current.id, this.issueForm.getRawValue()).subscribe({
      next: (updated) => {
        this.invoice.set(updated);
        this.issuing.set(false);
        this.showIssueForm.set(false);
      },
      error: (error: unknown) => {
        this.issuing.set(false);

        const problem = error instanceof HttpErrorResponse ? (error.error as ProblemDetails) : null;
        this.issueError.set(problem?.detail ?? 'Something went wrong issuing this invoice.');
        this.issueErrorFields.set(problem?.missingFields ?? null);
      },
    });
  }

  protected confirmPayment(): void {
    const current = this.invoice();
    if (!current || this.paymentForm.invalid) {
      return;
    }

    this.recordingPayment.set(true);
    this.paymentError.set(null);

    const { amount, receivedAt, reference } = this.paymentForm.getRawValue();

    this.paymentService
      .record(current.id, { amount: amount.toFixed(2), receivedAt, reference: reference || null })
      .subscribe({
        next: () => {
          this.recordingPayment.set(false);
          this.showPaymentForm.set(false);
          this.paymentForm.reset({ amount: 0, receivedAt: today(), reference: '' });
          this.load(current.id);
        },
        error: (error: unknown) => {
          this.recordingPayment.set(false);

          const problem =
            error instanceof HttpErrorResponse ? (error.error as ProblemDetails) : null;
          this.paymentError.set(problem?.detail ?? 'Something went wrong recording this payment.');
        },
      });
  }

  // Opened in a new tab rather than force-downloaded — most browsers render a PDF inline,
  // so the Owner can look at it before deciding to save it via their own PDF viewer.
  //
  // The tab is opened synchronously, in the same tick as the click, and redirected once the
  // blob arrives — window.open() from inside the subscribe callback would run after the async
  // HTTP round-trip completes, which every mainstream browser's popup blocker silently kills.
  protected viewPdf(): void {
    const current = this.invoice();
    if (!current) {
      return;
    }

    const pdfWindow = window.open('', '_blank');

    this.downloadingPdf.set(true);

    this.invoiceService.downloadPdf(current.id).subscribe({
      next: (blob) => {
        this.downloadingPdf.set(false);
        const url = URL.createObjectURL(blob);
        if (pdfWindow) {
          pdfWindow.location.href = url;
        }
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: () => {
        this.downloadingPdf.set(false);
        pdfWindow?.close();
      },
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
