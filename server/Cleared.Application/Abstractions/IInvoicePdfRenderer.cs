using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;

namespace Cleared.Application.Abstractions;

// Takes the same response DTOs already returned to the client, rather than Domain
// entities — the renderer needs exactly the data (and the same Money formatting) already
// being sent as JSON, and building it from anything else would mean formatting the same
// numbers twice, with two chances to disagree.
public interface IInvoicePdfRenderer
{
    byte[] Render(InvoiceResponse invoice, CustomerResponse customer, TenantResponse tenant);
}
