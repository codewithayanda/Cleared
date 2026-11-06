import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { Invoice, InvoiceStatus } from '@core/models/invoice.model';
import { InvoiceService } from '@core/services/invoice.service';
import { PaymentService } from '@core/services/payment.service';
import { aDraft, anInvoice } from '@core/testing/invoices';
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

describe('InvoiceDetail changing and deleting', () => {
  let deleted: string[];
  let remove: () => Observable<void>;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    deleted = [];
    remove = () => of(undefined);
  });

  async function open(invoice: Invoice) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'invoices/:id', component: InvoiceDetail }]),
        {
          provide: InvoiceService,
          useValue: {
            getById: () => of(invoice),
            delete: (id: string) => {
              deleted.push(id);

              return remove();
            },
          },
        },
        { provide: PaymentService, useValue: { list: () => of([]) } },
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/invoices/${invoice.id}`, InvoiceDetail);

    // Only now, because the harness opens the page through the same router.
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const element = harness.routeNativeElement as HTMLElement;
    const button = (name: string) =>
      [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === name);

    return {
      text: () => (element.textContent ?? '').replace(/\s+/g, ' '),
      editLink: () =>
        [...element.querySelectorAll('a')].find((a) => a.textContent?.trim() === 'Edit'),
      button,
      click: (name: string) => {
        button(name)!.click();
        harness.detectChanges();
      },
    };
  }

  it('offers to change a draft, with a link to the page that does it', async () => {
    const page = await open(aDraft());

    expect(page.editLink()!.getAttribute('href')).toBe('/invoices/invoice-1/edit');
  });

  it.each<InvoiceStatus>(['Issued', 'PartiallyPaid', 'Paid', 'Cancelled'])(
    'offers neither to change nor to delete an invoice that is %s',
    async (status) => {
      const page = await open(anInvoice({ status }));

      expect(page.editLink()).toBeUndefined();
      expect(page.button('Delete')).toBeUndefined();
    },
  );

  it('asks first, and deletes nothing until the person confirms', async () => {
    const page = await open(aDraft());

    page.click('Delete');

    expect(page.text()).toContain('Delete this draft?');
    expect(deleted).toEqual([]);

    page.click('Delete draft');

    expect(deleted).toEqual(['invoice-1']);
    expect(navigate).toHaveBeenCalledWith('/invoices');
  });

  it('keeps the draft when the person decides not to delete it', async () => {
    const page = await open(aDraft());

    page.click('Delete');
    page.click('Cancel');

    expect(page.text()).not.toContain('Delete this draft?');
    expect(page.button('Delete')).toBeDefined();
    expect(deleted).toEqual([]);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('treats a draft that is already gone as deleted', async () => {
    remove = () => throwError(() => new HttpErrorResponse({ status: 404 }));
    const page = await open(aDraft());

    page.click('Delete');
    page.click('Delete draft');

    expect(navigate).toHaveBeenCalledWith('/invoices');
  });

  it('says why an invoice issued in the meantime cannot be deleted, and stays on the page', async () => {
    remove = () => throwError(() => new HttpErrorResponse({ status: 409 }));
    const page = await open(aDraft());

    page.click('Delete');
    page.click('Delete draft');

    expect(page.text()).toContain('This invoice has been issued, so it can no longer be deleted.');
    expect(page.text()).toContain('credit note');
    expect(page.button('Delete draft')!.disabled).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('says it could not delete, and lets the person try again', async () => {
    remove = () => throwError(() => new HttpErrorResponse({ status: 500 }));
    const page = await open(aDraft());

    page.click('Delete');
    page.click('Delete draft');

    expect(page.text()).toContain('Could not delete the draft. Please try again.');
    expect(page.button('Delete draft')!.disabled).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });
});
