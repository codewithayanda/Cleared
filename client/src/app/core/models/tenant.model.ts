import { VatStatus } from '@core/models/auth.model';

export interface TenantResponse {
  id: string;
  companyName: string;
  tradingName: string | null;
  vatStatus: VatStatus;
  vatNumber: string | null;
  address: string | null;
  bankName: string | null;
  bankAccountNumber: string | null;
  bankBranchCode: string | null;
}

// address and the banking fields are settled after sign-up, via the company settings
// screen — not necessarily known at registration.
export interface UpdateTenantProfileRequest {
  address: string | null;
  bankName: string | null;
  bankAccountNumber: string | null;
  bankBranchCode: string | null;
}
