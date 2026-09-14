// receivedAt is supplied by the Owner, not defaulted to today: they are recording money
// that already arrived, often days earlier.
export interface RecordPaymentRequest {
  amount: string;
  receivedAt: string;
  reference: string | null;
}

export interface Payment {
  id: string;
  tenantId: string;
  invoiceId: string;
  method: string;
  amount: string;
  // The date the Owner says the money arrived, entered by hand, often after the fact.
  receivedAt: string;
  // When this record was captured in Cleared: a system timestamp, not a business fact.
  // Never conflated with receivedAt; see invoice-detail's payment table.
  recordedAt: string;
  reference: string | null;
}
