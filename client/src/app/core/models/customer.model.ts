export interface CreateCustomerRequest {
  name: string;
  vatNumber: string | null;
  email: string | null;
  address: string | null;
}

// Changing a customer takes the same details as adding one.
export type UpdateCustomerRequest = CreateCustomerRequest;

export interface Customer {
  id: string;
  tenantId: string;
  name: string;
  vatNumber: string | null;
  email: string | null;
  address: string | null;
}
