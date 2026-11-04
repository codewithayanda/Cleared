export type VatStatus = 'Registered' | 'NotRegistered';

export interface RegisterTenantRequest {
  companyName: string;
  vatStatus: VatStatus;
  vatNumber: string | null;
  tradingName: string | null;
}

export interface RegisterRequest {
  company: RegisterTenantRequest;
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

// The token is the secret in the emailed link.
export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}

// The token is the secret in the emailed link.
export interface VerifyEmailRequest {
  token: string;
}

export interface AuthResponse {
  accessToken: string;
}

// The signed-in person's own account.
export interface Account {
  email: string;
  emailVerified: boolean;
}
