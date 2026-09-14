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

// address and the banking fields are set after sign-up via the company settings screen.
// They are not always known at registration.
export interface UpdateTenantProfileRequest {
  address: string | null;
  bankName: string | null;
  bankAccountNumber: string | null;
  bankBranchCode: string | null;
}
