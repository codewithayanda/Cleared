import { Route } from '@angular/router';
import { CustomerForm } from './features/customers/customer-form/customer-form';
import { InvoiceForm } from './features/invoices/invoice-form/invoice-form';
import { routes } from './app.routes';

// A screen that is built but not given a route is a screen nobody can open, so the routes are checked.
describe('routes', () => {
  const signedIn = (): Route[] =>
    routes.find((route) => route.path === '' && route.children)!.children!;

  it('open the customer form for changing a customer, behind sign-in', async () => {
    const edit = signedIn().find((route) => route.path === 'customers/:id/edit');

    expect(edit).toBeDefined();
    expect(await (edit!.loadComponent as () => Promise<unknown>)()).toBe(CustomerForm);
  });

  it('open the invoice form for changing a draft, behind sign-in', async () => {
    const edit = signedIn().find((route) => route.path === 'invoices/:id/edit');

    expect(edit).toBeDefined();
    expect(await (edit!.loadComponent as () => Promise<unknown>)()).toBe(InvoiceForm);
  });
});
