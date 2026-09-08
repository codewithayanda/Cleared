import { Routes } from '@angular/router';
import { authGuard } from '@core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: 'get-started',
    loadComponent: () =>
      import('./features/auth/get-started/get-started').then((m) => m.GetStarted),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./core/layout/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'customers',
        loadComponent: () =>
          import('./features/customers/customer-list/customer-list').then((m) => m.CustomerList),
      },
      {
        path: 'customers/new',
        loadComponent: () =>
          import('./features/customers/customer-form/customer-form').then((m) => m.CustomerForm),
      },
      {
        path: 'invoices',
        loadComponent: () =>
          import('./features/invoices/invoice-list/invoice-list').then((m) => m.InvoiceList),
      },
      {
        path: 'invoices/new',
        loadComponent: () =>
          import('./features/invoices/invoice-form/invoice-form').then((m) => m.InvoiceForm),
      },
      {
        path: 'invoices/:id',
        loadComponent: () =>
          import('./features/invoices/invoice-detail/invoice-detail').then((m) => m.InvoiceDetail),
      },
    ],
  },
  { path: '**', redirectTo: 'login' },
];
