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
  receivedAt: string;
  reference: string | null;
}
