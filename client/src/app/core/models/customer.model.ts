export interface CreateCustomerRequest {
  name: string;
  vatNumber: string | null;
  email: string | null;
}

export interface Customer {
  id: string;
  tenantId: string;
  name: string;
  vatNumber: string | null;
  email: string | null;
}
