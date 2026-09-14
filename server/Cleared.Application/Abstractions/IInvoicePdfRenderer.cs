using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;

namespace Cleared.Application.Abstractions;

// Takes the response DTOs already returned to the client, not Domain entities: the renderer
// needs the same data and the same Money formatting as the JSON, and building it from
// anything else would format the same numbers twice.
public interface IInvoicePdfRenderer
{
    byte[] Render(InvoiceResponse invoice, CustomerResponse customer, TenantResponse tenant);
}
