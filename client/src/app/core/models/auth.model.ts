export type VatStatus = 'Registered' | 'NotRegistered';

export interface RegisterTenantRequest {
  companyName: string;
  vatStatus: VatStatus;
  vatNumber: string | null;
  tradingName: string | null;
}

export interface RegisterUserRequest {
  tenantId: string;
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
}
