import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Customer } from '@core/models/customer.model';
import { CustomerService } from '@core/services/customer.service';
import { CustomerList } from './customer-list';

const customer = (id: string, name: string): Customer => ({
  id,
  tenantId: 't1',
  name,
  vatNumber: null,
  email: null,
  address: null,
});

describe('CustomerList', () => {
  function render(customers: Customer[]) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: CustomerService, useValue: { list: () => of(customers) } },
      ],
    });
    const fixture = TestBed.createComponent(CustomerList);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('gives every customer a link to change their details', () => {
    const element = render([customer('c1', 'Beta Co'), customer('c2', 'Gamma Ltd')]);

    const links = [...element.querySelectorAll<HTMLAnchorElement>('tbody a')];

    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/customers/c1/edit',
      '/customers/c2/edit',
    ]);
  });

  it('says which customer each link is for, to someone who cannot see the row', () => {
    const element = render([customer('c1', 'Beta Co')]);

    expect(element.querySelector('tbody a')?.textContent).toContain('Edit Beta Co');
  });

  it('still welcomes someone with no customers', () => {
    const element = render([]);

    expect(element.textContent).toContain('No customers yet');
  });
});
