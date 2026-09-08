export type InvoiceStatus = 'Draft' | 'Issued' | 'PartiallyPaid' | 'Paid' | 'Cancelled';

export interface CreateInvoiceLineRequest {
  description: string;
  quantity: number;
  // A string on the wire, deliberately — see the API contract conventions. JavaScript's
  // float64 can silently corrupt a decimal, so money is never a JSON number in this app.
  unitPrice: string;
}

export interface CreateInvoiceRequest {
  customerId: string;
  lines: CreateInvoiceLineRequest[];
}

export interface InvoiceLine {
  id: string;
  description: string;
  quantity: number;
  unitPrice: string;
  lineSubtotal: string;
}

export interface Invoice {
  id: string;
  tenantId: string;
  customerId: string;
  status: InvoiceStatus;
  number: string | null;
  issueDate: string | null;
  dueDate: string | null;
  subtotal: string;
  lines: InvoiceLine[];
}

export interface IssueInvoiceRequest {
  issueDate: string;
  dueDate: string;
}
