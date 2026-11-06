import { Invoice, InvoiceLine } from '@core/models/invoice.model';

// A line from a business that is not VAT registered, for a test to change only what it is about.
export function anInvoiceLine(overrides: Partial<InvoiceLine> = {}): InvoiceLine {
  return {
    id: 'line-1',
    description: 'Consulting',
    quantity: 2,
    unitPrice: '500.00',
    lineSubtotal: '1000.00',
    vatTreatment: 'NotApplicable',
    lineVat: '0.00',
    lineTotal: '1000.00',
    ...overrides,
  };
}

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

// A draft: not issued yet, so it has no number, no dates and no document type.
export function aDraft(overrides: Partial<Invoice> = {}): Invoice {
  return anInvoice({
    status: 'Draft',
    documentType: null,
    number: null,
    issueDate: null,
    supplyDate: null,
    dueDate: null,
    subtotal: '1000.00',
    total: '1000.00',
    balanceDue: '1000.00',
    lines: [anInvoiceLine()],
    ...overrides,
  });
}
