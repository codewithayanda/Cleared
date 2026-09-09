using Cleared.Application.Abstractions;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Cleared.Infrastructure.Documents;

// QuestPDF.Settings.License is set once at startup (see Program.cs) — Community edition,
// free under $1M USD annual revenue (see the ADR this needs, and the package comment in
// Directory.Packages.props).
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    public byte[] Render(InvoiceResponse invoice, CustomerResponse customer, TenantResponse tenant)
    {
        var title = invoice.DocumentType == "TaxInvoice" ? "TAX INVOICE" : "INVOICE";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text(title).FontSize(20).Bold();
                    column.Item().PaddingTop(5).Text(tenant.CompanyName).FontSize(14).Bold();
                    if (tenant.TradingName is { } tradingName)
                    {
                        column.Item().Text($"t/a {tradingName}");
                    }

                    if (tenant.Address is { } tenantAddress)
                    {
                        column.Item().Text(tenantAddress);
                    }

                    if (tenant.VatNumber is { } tenantVatNumber)
                    {
                        column.Item().Text($"VAT no: {tenantVatNumber}");
                    }
                });

                page.Content().PaddingVertical(15).Column(column =>
                {
                    column.Spacing(15);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(billTo =>
                        {
                            billTo.Item().Text("Bill to").Bold();
                            billTo.Item().Text(customer.Name);
                            if (customer.Address is { } address)
                            {
                                billTo.Item().Text(address);
                            }

                            if (customer.VatNumber is { } customerVatNumber)
                            {
                                billTo.Item().Text($"VAT no: {customerVatNumber}");
                            }
                        });

                        row.RelativeItem().Column(details =>
                        {
                            details.Item().Text($"Invoice number: {invoice.Number}");
                            details.Item().Text($"Issue date: {invoice.IssueDate}");
                            details.Item().Text($"Due date: {invoice.DueDate}");
                            if (invoice.VatRateApplied is { } vatRate)
                            {
                                details.Item().Text($"VAT rate: {decimal.Parse(vatRate) * 100:F0}%");
                            }
                        });
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Description").Bold();
                            header.Cell().AlignRight().Text("Qty").Bold();
                            header.Cell().AlignRight().Text("Unit price").Bold();
                            header.Cell().AlignRight().Text("VAT").Bold();
                            header.Cell().AlignRight().Text("VAT amount").Bold();
                            header.Cell().AlignRight().Text("Line total").Bold();
                        });

                        foreach (var line in invoice.Lines)
                        {
                            table.Cell().Text(line.Description);
                            table.Cell().AlignRight().Text(line.Quantity.ToString("0.####"));
                            table.Cell().AlignRight().Text($"R {line.UnitPrice}");
                            table.Cell().AlignRight().Text(line.VatTreatment);
                            table.Cell().AlignRight().Text($"R {line.LineVat}");
                            table.Cell().AlignRight().Text($"R {line.LineTotal}");
                        }
                    });

                    column.Item().AlignRight().Column(totals =>
                    {
                        totals.Item().Text($"Subtotal: R {invoice.Subtotal}");
                        totals.Item().Text($"VAT: R {invoice.VatTotal}");
                        totals.Item().PaddingTop(5).Text($"Total: R {invoice.Total}").FontSize(14).Bold();
                    });

                    // Not required to issue an invoice at all (see Tenant.BankName's own
                    // note) — but without it, there's a total on this page and no way for
                    // whoever's reading it to actually pay it by EFT.
                    if (tenant.BankName is not null || tenant.BankAccountNumber is not null
                        || tenant.BankBranchCode is not null)
                    {
                        column.Item().PaddingTop(10).BorderTop(1).PaddingTop(10).Column(banking =>
                        {
                            banking.Item().Text("Banking details").Bold();
                            if (tenant.BankName is { } bankName)
                            {
                                banking.Item().Text($"Bank: {bankName}");
                            }

                            if (tenant.BankAccountNumber is { } accountNumber)
                            {
                                banking.Item().Text($"Account number: {accountNumber}");
                            }

                            if (tenant.BankBranchCode is { } branchCode)
                            {
                                banking.Item().Text($"Branch code: {branchCode}");
                            }

                            if (invoice.Number is { } reference)
                            {
                                banking.Item().Text($"Payment reference: {reference}");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by Cleared").FontSize(8);
                });
            });
        });

        return document.GeneratePdf();
    }
}
