export type InvoiceStatus = 'Draft' | 'Issued' | 'PartiallyPaid' | 'Paid' | 'Cancelled';

// NotApplicable isn't offered when creating a line. The server forces every line to it for
// a tenant that isn't VAT registered, whatever the client sends.
export type VatTreatment = 'Standard' | 'ZeroRated' | 'Exempt' | 'NotApplicable';

// Derived server-side from the tenant's VAT status at Issue(), never a client choice.
// See Cleared.Domain.Invoicing.DocumentType.
export type DocumentType = 'Invoice' | 'TaxInvoice';

export interface CreateInvoiceLineRequest {
  description: string;
  quantity: number;
  // A string on the wire: float64 can silently corrupt a decimal, so money is never a
  // JSON number in this app.
  unitPrice: string;
  vatTreatment: VatTreatment;
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
  vatTreatment: VatTreatment;
  lineVat: string;
  lineTotal: string;
}

export interface Invoice {
  id: string;
  tenantId: string;
  customerId: string;
  status: InvoiceStatus;
  documentType: DocumentType | null;
  number: string | null;
  issueDate: string | null;
  supplyDate: string | null;
  dueDate: string | null;
  vatRateApplied: string | null;
  subtotal: string;
  vatTotal: string;
  total: string;
  amountPaid: string;
  balanceDue: string;
  lines: InvoiceLine[];
}

export interface IssueInvoiceRequest {
  issueDate: string;
  dueDate: string;
}

// Mirrors TaxInvoiceValidationException's shape on the wire (see DomainExceptionHandler's
// ProblemDetails.Extensions): the fields blocking the issue, not a generic error string.
export interface ProblemDetails {
  title?: string;
  detail?: string;
  missingFields?: string[];
}
