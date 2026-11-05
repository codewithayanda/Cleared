import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { Invoice } from '@core/models/invoice.model';
import { InvoiceService } from '@core/services/invoice.service';
import { PaymentService } from '@core/services/payment.service';
import { anInvoice } from '@core/testing/invoices';
import { InvoiceDetail } from './invoice-detail';

describe('InvoiceDetail', () => {
  async function open(invoice: Invoice) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'invoices/:id', component: InvoiceDetail }]),
        { provide: InvoiceService, useValue: { getById: () => of(invoice) } },
        { provide: PaymentService, useValue: { list: () => of([]) } },
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/invoices/${invoice.id}`, InvoiceDetail);
    const element = harness.routeNativeElement as HTMLElement;

    return {
      heading: () => element.querySelector('h1')!.parentElement!.textContent!.replace(/\s+/g, ' '),
      dueDate: () =>
        [...element.querySelectorAll('dt')].find(
          (label) => label.textContent!.trim() === 'Due date',
        )!.nextElementSibling!,
    };
  }

  it('flags an overdue invoice with a pill and a red due date', async () => {
    const { heading, dueDate } = await open(anInvoice({ dueDate: '2026-10-15', isOverdue: true }));

    expect(heading()).toContain('Overdue');
    expect(dueDate().classList).toContain('text-status-overdue');
  });

  it('flags nothing on an invoice that is not overdue', async () => {
    const { heading, dueDate } = await open(anInvoice());

    expect(heading()).not.toContain('Overdue');
    expect(dueDate().classList).not.toContain('text-status-overdue');
  });

  it('goes by what the server says and never by the due date', async () => {
    const paid = await open(anInvoice({ status: 'Paid', dueDate: '2020-01-01', isOverdue: false }));

    expect(paid.heading()).not.toContain('Overdue');
    expect(paid.dueDate().classList).not.toContain('text-status-overdue');
  });
});
