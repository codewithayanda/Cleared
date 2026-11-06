import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { Customer } from '@core/models/customer.model';
import { CreateInvoiceRequest, Invoice, UpdateInvoiceRequest } from '@core/models/invoice.model';
import { CustomerService } from '@core/services/customer.service';
import { InvoiceService } from '@core/services/invoice.service';
import { aDraft, anInvoice, anInvoiceLine } from '@core/testing/invoices';
import { InvoiceForm } from './invoice-form';

const BETA: Customer = {
  id: 'c1',
  tenantId: 't1',
  name: 'Beta Co',
  vatNumber: null,
  email: null,
  address: null,
};
const GAMMA: Customer = { ...BETA, id: 'c2', name: 'Gamma Ltd' };

describe('InvoiceForm', () => {
  let asked: string[];
  let started: { request: CreateInvoiceRequest; key: string }[];
  let changes: { id: string; request: UpdateInvoiceRequest }[];
  let load: () => Observable<Invoice>;
  let save: () => Observable<Invoice>;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    asked = [];
    started = [];
    changes = [];
    load = () => of(aDraft({ id: 'inv-1', customerId: 'c1' }));
    save = () => of(aDraft({ id: 'inv-1', customerId: 'c1' }));

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'invoices/new', component: InvoiceForm },
          { path: 'invoices/:id/edit', component: InvoiceForm },
        ]),
        { provide: CustomerService, useValue: { list: () => of([BETA, GAMMA]) } },
        {
          provide: InvoiceService,
          useValue: {
            getById: (id: string) => {
              asked.push(id);

              return load();
            },
            create: (request: CreateInvoiceRequest, key: string) => {
              started.push({ request, key });

              return save();
            },
            update: (id: string, request: UpdateInvoiceRequest) => {
              changes.push({ id, request });

              return save();
            },
          },
        },
      ],
    });
  });

  async function open(url: string) {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, InvoiceForm);

    // Only now, because the harness opens the page through the same router.
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const element = harness.routeNativeElement as HTMLElement;
    const inputs = (name: string) => [
      ...element.querySelectorAll<HTMLInputElement>(`input[formControlName="${name}"]`),
    ];
    const customer = () => element.querySelector<HTMLSelectElement>('#customerId')!;
    const button = () => element.querySelector<HTMLButtonElement>('button[type="submit"]');
    const type = (input: HTMLInputElement, value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };

    return {
      text: () => (element.textContent ?? '').replace(/\s+/g, ' '),
      customer,
      button,
      descriptions: () => inputs('description').map((input) => input.value),
      quantities: () => inputs('quantity').map((input) => input.value),
      prices: () => inputs('unitPrice').map((input) => input.value),
      treatments: () =>
        [
          ...element.querySelectorAll<HTMLSelectElement>('select[formControlName="vatTreatment"]'),
        ].map((select) => select.value),
      cancelLink: () =>
        [...element.querySelectorAll('a')]
          .find((link) => link.textContent?.trim() === 'Cancel')
          ?.getAttribute('href'),
      choose: (id: string) => {
        customer().value = id;
        customer().dispatchEvent(new Event('change'));
      },
      describe: (index: number, value: string) => type(inputs('description')[index], value),
      price: (index: number, value: string) => type(inputs('unitPrice')[index], value),
      submit: () => {
        button()!.click();
        harness.detectChanges();
      },
    };
  }

  describe('changing a draft', () => {
    it('asks for the draft named in the address and fills in its customer and lines', async () => {
      load = () =>
        of(
          aDraft({
            id: 'inv-1',
            customerId: 'c2',
            lines: [
              anInvoiceLine({
                id: 'l1',
                description: 'Design',
                quantity: 3,
                unitPrice: '100.00',
                vatTreatment: 'ZeroRated',
              }),
              anInvoiceLine({
                id: 'l2',
                description: 'Hosting',
                quantity: 1,
                unitPrice: '50.50',
                vatTreatment: 'Exempt',
              }),
            ],
          }),
        );

      const page = await open('/invoices/inv-1/edit');

      expect(asked).toEqual(['inv-1']);
      expect(page.customer().value).toBe('c2');
      expect(page.descriptions()).toEqual(['Design', 'Hosting']);
      expect(page.quantities()).toEqual(['3', '1']);
      expect(page.prices()).toEqual(['100', '50.5']);
      expect(page.treatments()).toEqual(['ZeroRated', 'Exempt']);
      expect(page.text()).toContain('Edit draft invoice');
      expect(page.text()).toContain('Save changes');
    });

    it('opens a line saved as not applicable with the standard treatment', async () => {
      load = () => of(aDraft({ lines: [anInvoiceLine({ vatTreatment: 'NotApplicable' })] }));

      const page = await open('/invoices/inv-1/edit');

      expect(page.treatments()).toEqual(['Standard']);
    });

    it('still offers a line to type into when the draft has none', async () => {
      load = () => of(aDraft({ lines: [] }));

      const page = await open('/invoices/inv-1/edit');

      expect(page.descriptions()).toEqual(['']);
    });

    it('saves the new customer and lines against that draft, with prices as text, and goes to it', async () => {
      const page = await open('/invoices/inv-1/edit');

      page.choose('c2');
      page.describe(0, 'Design work');
      page.price(0, '75.5');
      page.submit();

      expect(changes).toEqual([
        {
          id: 'inv-1',
          request: {
            customerId: 'c2',
            lines: [
              {
                description: 'Design work',
                quantity: 2,
                unitPrice: '75.50',
                vatTreatment: 'Standard',
              },
            ],
          },
        },
      ]);
      expect(started).toHaveLength(0);
      expect(navigate).toHaveBeenCalledWith(['/invoices', 'inv-1']);
    });

    it('goes back to the draft and not to the list when the person cancels', async () => {
      const page = await open('/invoices/inv-1/edit');

      expect(page.cancelLink()).toBe('/invoices/inv-1');
    });

    it('says the draft could not be found, and offers no form to fill in', async () => {
      load = () => throwError(() => new HttpErrorResponse({ status: 404 }));

      const page = await open('/invoices/inv-1/edit');

      expect(page.text()).toContain("This invoice couldn't be found.");
      expect(page.button()).toBeNull();
    });

    it('says an issued invoice can no longer be changed, and offers no form to fill in', async () => {
      load = () => of(anInvoice({ id: 'inv-1', status: 'Issued' }));

      const page = await open('/invoices/inv-1/edit');

      expect(page.text()).toContain(
        'This invoice has been issued, so it can no longer be changed.',
      );
      expect(page.text()).toContain('credit note');
      expect(page.button()).toBeNull();
    });

    it('says the invoice was issued when saving finds that it was issued in the meantime', async () => {
      save = () => throwError(() => new HttpErrorResponse({ status: 409 }));
      const page = await open('/invoices/inv-1/edit');

      page.submit();

      expect(page.text()).toContain(
        'This invoice has been issued, so it can no longer be changed.',
      );
      expect(page.button()!.disabled).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('says the draft no longer exists when saving finds it deleted in the meantime', async () => {
      save = () => throwError(() => new HttpErrorResponse({ status: 404 }));
      const page = await open('/invoices/inv-1/edit');

      page.submit();

      expect(page.text()).toContain('This draft no longer exists.');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('says it could not save, and lets the person try again', async () => {
      save = () => throwError(() => new HttpErrorResponse({ status: 500 }));
      const page = await open('/invoices/inv-1/edit');

      page.submit();

      expect(page.text()).toContain('Could not save the invoice. Please try again.');
      expect(page.button()!.disabled).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
    });
  });

  describe('starting a draft', () => {
    async function openFilledIn() {
      const page = await open('/invoices/new');
      page.choose('c1');
      page.describe(0, 'Design');
      page.price(0, '50');

      return page;
    }

    it('is for starting one when no draft is named in the address', async () => {
      const page = await open('/invoices/new');

      expect(asked).toEqual([]);
      expect(page.text()).toContain('New invoice');
      expect(page.text()).toContain('Save draft');
      expect(page.text()).not.toContain('Edit draft invoice');
      expect(page.cancelLink()).toBe('/invoices');
    });

    it('starts the draft with an Idempotency-Key and goes to it', async () => {
      const page = await openFilledIn();

      page.submit();

      expect(started).toHaveLength(1);
      expect(started[0].request).toEqual({
        customerId: 'c1',
        lines: [
          { description: 'Design', quantity: 1, unitPrice: '50.00', vatTreatment: 'Standard' },
        ],
      });
      expect(started[0].key).not.toBe('');
      expect(changes).toHaveLength(0);
      expect(navigate).toHaveBeenCalledWith(['/invoices', 'inv-1']);
    });

    it('sends the same Idempotency-Key again when a failed save is tried again', async () => {
      save = () => throwError(() => new HttpErrorResponse({ status: 500 }));
      const page = await openFilledIn();

      page.submit();
      page.submit();

      expect(started).toHaveLength(2);
      expect(started[1].key).toBe(started[0].key);
      expect(page.text()).toContain('Could not save the invoice. Please try again.');
    });
  });
});
