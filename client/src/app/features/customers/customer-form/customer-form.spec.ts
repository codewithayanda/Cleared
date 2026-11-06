import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import {
  CreateCustomerRequest,
  Customer,
  UpdateCustomerRequest,
} from '@core/models/customer.model';
import { CustomerService } from '@core/services/customer.service';
import { CustomerForm } from './customer-form';

describe('CustomerForm', () => {
  let sent: CreateCustomerRequest[];

  beforeEach(() => {
    sent = [];
    TestBed.configureTestingModule({
      imports: [CustomerForm],
      providers: [
        provideRouter([]),
        {
          provide: CustomerService,
          useValue: {
            create: (request: CreateCustomerRequest) => {
              sent.push(request);

              return new Subject<Customer>();
            },
          },
        },
      ],
    });
  });

  function open() {
    const fixture = TestBed.createComponent(CustomerForm);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const fill = (selector: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };

    const submit = (email: string) => {
      fill('#name', 'Jane Doe');
      fill('#email', email);
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      fixture.detectChanges();
    };

    return { submit, text: () => element.textContent ?? '' };
  }

  it.each(['not-an-email', 'jane@example'])(
    'sends nothing and says why when the email is %s',
    (email) => {
      const { submit, text } = open();

      submit(email);

      expect(sent).toHaveLength(0);
      expect(text()).toContain('Enter a valid email address.');
    },
  );

  it('sends null when the email is left empty', () => {
    const { submit, text } = open();

    submit('');

    expect(sent).toHaveLength(1);
    expect(sent[0].email).toBeNull();
    expect(text()).not.toContain('Enter a valid email address.');
  });

  it('is for adding someone when no customer is named in the address', () => {
    const { text } = open();

    expect(text()).toContain('New customer');
    expect(text()).toContain('Save customer');
    expect(text()).not.toContain('Invoices you have already issued');
  });
});

describe('CustomerForm changing a customer', () => {
  const beta: Customer = {
    id: 'c1',
    tenantId: 't1',
    name: 'Beta Co',
    vatNumber: '4987654321',
    email: 'ap@beta.test',
    address: '1 Main Road',
  };

  let asked: string[];
  let changes: { id: string; request: UpdateCustomerRequest }[];
  let created: number;
  let load: () => Observable<Customer>;
  let save: () => Observable<Customer>;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    asked = [];
    changes = [];
    created = 0;
    load = () => of(beta);
    save = () => of(beta);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'customers/:id/edit', component: CustomerForm }]),
        {
          provide: CustomerService,
          useValue: {
            getById: (id: string) => {
              asked.push(id);

              return load();
            },
            update: (id: string, request: UpdateCustomerRequest) => {
              changes.push({ id, request });

              return save();
            },
            create: () => {
              created++;

              return of(beta);
            },
          },
        },
      ],
    });
  });

  async function open() {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/customers/c1/edit', CustomerForm);

    // Only now, because the harness opens the page through this same method.
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const element = harness.routeNativeElement as HTMLElement;
    const field = (selector: string) => element.querySelector<HTMLInputElement>(selector)!;

    return {
      field,
      text: () => (element.textContent ?? '').replace(/\s+/g, ' '),
      type: (selector: string, value: string) => {
        field(selector).value = value;
        field(selector).dispatchEvent(new Event('input'));
      },
      save: () => {
        element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
        harness.detectChanges();
      },
      button: () => element.querySelector<HTMLButtonElement>('button[type="submit"]'),
    };
  }

  it('asks for the customer named in the address and fills the form with their details', async () => {
    const { field, text } = await open();

    expect(asked).toEqual(['c1']);
    expect(field('#name').value).toBe('Beta Co');
    expect(field('#email').value).toBe('ap@beta.test');
    expect(field('#vatNumber').value).toBe('4987654321');
    expect(field('#address').value).toBe('1 Main Road');
    expect(text()).toContain('Edit customer');
  });

  it('tells the owner that invoices already issued keep the details they were issued with', async () => {
    const { text } = await open();

    expect(text()).toContain(
      'Invoices you have already issued keep the details they were issued with.',
    );
  });

  it('saves the changes against that customer and goes back to the list', async () => {
    const { type, save: submit, text } = await open();
    expect(text()).toContain('Save changes');

    type('#name', 'Gamma Ltd');
    submit();

    expect(changes).toEqual([
      {
        id: 'c1',
        request: {
          name: 'Gamma Ltd',
          email: 'ap@beta.test',
          vatNumber: '4987654321',
          address: '1 Main Road',
        },
      },
    ]);
    expect(created).toBe(0);
    expect(navigate).toHaveBeenCalledWith('/customers');
  });

  it('sends null for a detail that was cleared', async () => {
    const { type, save: submit } = await open();

    type('#vatNumber', '');
    type('#address', '');
    submit();

    expect(changes[0].request.vatNumber).toBeNull();
    expect(changes[0].request.address).toBeNull();
  });

  it('sends nothing and says why when the email was changed to one that cannot be real', async () => {
    const { type, save: submit, text } = await open();

    type('#email', 'not-an-email');
    submit();

    expect(changes).toHaveLength(0);
    expect(text()).toContain('Enter a valid email address.');
  });

  it('says the customer could not be found, and offers no form to fill in', async () => {
    load = () => throwError(() => new HttpErrorResponse({ status: 404 }));

    const { text, button } = await open();

    expect(text()).toContain("This customer couldn't be found.");
    expect(button()).toBeNull();
  });

  it('says it could not save, and lets the person try again', async () => {
    save = () => throwError(() => new HttpErrorResponse({ status: 500 }));
    const { save: submit, text, button } = await open();

    submit();

    expect(text()).toContain('Could not save the customer. Please try again.');
    expect(button()!.disabled).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });
});
