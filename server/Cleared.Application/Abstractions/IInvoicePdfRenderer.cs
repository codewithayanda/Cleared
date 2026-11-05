using Cleared.Application.Invoices;

namespace Cleared.Application.Abstractions;

// Draws a document whose words InvoiceDocumentBuilder has already decided, so the layout holds no
// rules about what an invoice says and no number or date is formatted twice.
public interface IInvoicePdfRenderer
{
    byte[] Render(InvoiceDocument document);
}
