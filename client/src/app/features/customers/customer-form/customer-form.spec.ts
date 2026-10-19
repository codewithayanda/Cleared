import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { CreateCustomerRequest, Customer } from '@core/models/customer.model';
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
});
