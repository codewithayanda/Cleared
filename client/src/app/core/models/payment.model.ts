// receivedAt is supplied by the Owner, not defaulted to today — they're recording money
// that already arrived, often days ago, by the time they get to entering it.
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
  // The date the Owner says the money arrived — entered by hand, often after the fact.
  receivedAt: string;
  // When this record was captured in Cleared — a real timestamp, not a business fact.
  // Deliberately never conflated with receivedAt; see invoice-detail's payment table.
  recordedAt: string;
  reference: string | null;
}
