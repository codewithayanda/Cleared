import { Invoice } from '@core/models/invoice.model';

// An issued invoice that is on time, for a test to change only what it is about.
export function anInvoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    id: 'invoice-1',
    tenantId: 'tenant-1',
    customerId: 'customer-1',
    status: 'Issued',
    documentType: 'Invoice',
    number: 'INV-2026-0001',
    issueDate: '2026-10-01',
    supplyDate: '2026-10-01',
    dueDate: '2026-12-01',
    vatRateApplied: null,
    subtotal: '100.00',
    vatTotal: '0.00',
    total: '100.00',
    amountPaid: '0.00',
    balanceDue: '100.00',
    isOverdue: false,
    lines: [],
    ...overrides,
  };
}
