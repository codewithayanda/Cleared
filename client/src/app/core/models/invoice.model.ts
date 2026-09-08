export type InvoiceStatus = 'Draft' | 'Issued' | 'PartiallyPaid' | 'Paid' | 'Cancelled';

// NotApplicable isn't offered as a choice when creating a line — it's what the server
// forces every line to for a tenant that isn't VAT registered, regardless of what's sent.
export type VatTreatment = 'Standard' | 'ZeroRated' | 'Exempt' | 'NotApplicable';

// Which one an invoice becomes is derived server-side from the tenant's VAT status at
// Issue() and is never a client choice — see Cleared.Domain.Invoicing.DocumentType.
export type DocumentType = 'Invoice' | 'TaxInvoice';

export interface CreateInvoiceLineRequest {
  description: string;
  quantity: number;
  // A string on the wire, deliberately — see the API contract conventions. JavaScript's
  // float64 can silently corrupt a decimal, so money is never a JSON number in this app.
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
  lines: InvoiceLine[];
}

export interface IssueInvoiceRequest {
  issueDate: string;
  dueDate: string;
}

// Mirrors Cleared.Domain.Invoicing.TaxInvoiceValidationException's shape on the wire
// (see DomainExceptionHandler's ProblemDetails.Extensions) — the specific fields
// blocking a tax invoice from issuing, not just a generic error string.
export interface ProblemDetails {
  title?: string;
  detail?: string;
  missingFields?: string[];
}
