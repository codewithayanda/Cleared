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
//
// Colors are the same steel/gold tokens as client/src/styles.css's @theme block, deliberately
// kept in sync by hand — this is the one other place a customer sees Cleared's branding, and
// it should look like it came from the same product as the web app.
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    private const string SteelDark = "#1a1f26";
    private const string SteelText = "#313945";
    private const string SteelMuted = "#5c6879";
    private const string SteelBorder = "#ccd1d9";
    private const string SteelBand = "#e4e7ec";
    private const string SteelPale = "#f6f7f9";
    private const string Gold = "#a8791a";
    private const string White = "#ffffff";

    public byte[] Render(InvoiceResponse invoice, CustomerResponse customer, TenantResponse tenant)
    {
        var title = invoice.DocumentType == "TaxInvoice" ? "TAX INVOICE" : "INVOICE";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(SteelText));

                page.Header().Background(SteelDark).Padding(30).Row(row =>
                {
                    row.RelativeItem().Column(company =>
                    {
                        company.Item().Text(tenant.CompanyName).FontSize(18).Bold().FontColor(White);
                        if (tenant.TradingName is { } tradingName)
                        {
                            company.Item().PaddingTop(2).Text($"t/a {tradingName}").FontColor(SteelBorder);
                        }

                        if (tenant.Address is { } tenantAddress)
                        {
                            company.Item().PaddingTop(6).Text(tenantAddress).FontSize(9).FontColor(SteelBorder);
                        }

                        if (tenant.VatNumber is { } tenantVatNumber)
                        {
                            company.Item().Text($"VAT no: {tenantVatNumber}").FontSize(9).FontColor(SteelBorder);
                        }
                    });

                    row.ConstantItem(170).Column(heading =>
                    {
                        heading.Item().AlignRight().Text(title).FontSize(20).Bold().FontColor(Gold);
                        if (invoice.Number is { } number)
                        {
                            heading.Item().PaddingTop(8).AlignRight().Text(number).FontSize(12).Bold().FontColor(White);
                        }
                    });
                });

                page.Content().Padding(30).Column(column =>
                {
                    column.Spacing(20);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(billTo =>
                        {
                            billTo.Item().Text("BILL TO").FontSize(8).Bold().FontColor(SteelMuted);
                            billTo.Item().PaddingTop(4).Text(customer.Name).FontSize(12).Bold().FontColor(SteelDark);
                            if (customer.Address is { } address)
                            {
                                billTo.Item().PaddingTop(2).Text(address);
                            }

                            if (customer.VatNumber is { } customerVatNumber)
                            {
                                billTo.Item().Text($"VAT no: {customerVatNumber}");
                            }
                        });

                        row.ConstantItem(180).Column(details =>
                        {
                            if (invoice.IssueDate is { } issueDate)
                            {
                                MetaRow(details, "Issue date", issueDate.ToString());
                            }

                            if (invoice.DueDate is { } dueDate)
                            {
                                MetaRow(details, "Due date", dueDate.ToString());
                            }

                            if (invoice.VatRateApplied is { } vatRate)
                            {
                                MetaRow(details, "VAT rate", $"{decimal.Parse(vatRate) * 100:F0}%");
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
                            HeaderCell(header, "Description", alignRight: false);
                            HeaderCell(header, "Qty");
                            HeaderCell(header, "Unit price");
                            HeaderCell(header, "VAT");
                            HeaderCell(header, "VAT amount");
                            HeaderCell(header, "Line total");
                        });

                        var index = 0;
                        foreach (var line in invoice.Lines)
                        {
                            var background = index % 2 == 0 ? White : SteelPale;
                            index++;

                            BodyCell(table, background, line.Description, alignRight: false);
                            BodyCell(table, background, line.Quantity.ToString("0.####"));
                            BodyCell(table, background, $"R {line.UnitPrice}");
                            BodyCell(table, background, line.VatTreatment);
                            BodyCell(table, background, $"R {line.LineVat}");
                            BodyCell(table, background, $"R {line.LineTotal}", bold: true);
                        }
                    });

                    column.Item().AlignRight().Width(220).Column(totals =>
                    {
                        totals.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Subtotal").FontColor(SteelMuted);
                            r.RelativeItem().AlignRight().Text($"R {invoice.Subtotal}");
                        });
                        totals.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text("VAT").FontColor(SteelMuted);
                            r.RelativeItem().AlignRight().Text($"R {invoice.VatTotal}");
                        });
                        totals.Item().PaddingTop(10).Background(SteelDark).Padding(10).Row(r =>
                        {
                            r.RelativeItem().Text("Total due").FontColor(White).Bold();
                            r.RelativeItem().AlignRight().Text($"R {invoice.Total}").FontSize(13).Bold().FontColor(White);
                        });
                    });

                    // Not required to issue an invoice at all (see Tenant.BankName's own
                    // note) — but without it, there's a total on this page and no way for
                    // whoever's reading it to actually pay it by EFT. Given banking details
                    // by design (see D-D / EFT-first payment model) — it's the single most
                    // actionable thing on the page, so it earns its own callout, not just
                    // another plain text block.
                    if (tenant.BankName is not null || tenant.BankAccountNumber is not null
                        || tenant.BankBranchCode is not null)
                    {
                        column.Item().Background(SteelPale).BorderLeft(3).BorderColor(Gold).Padding(14)
                            .Column(banking =>
                            {
                                banking.Item().Text("BANKING DETAILS").FontSize(8).Bold().FontColor(SteelMuted);

                                if (tenant.BankName is { } bankName)
                                {
                                    banking.Item().PaddingTop(4).Text(bankName).Bold();
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
                                    banking.Item().PaddingTop(4).Text(text =>
                                    {
                                        text.Span("Payment reference: ").FontColor(SteelMuted);
                                        text.Span(reference).Bold();
                                    });
                                }
                            });
                    }
                });

                page.Footer().BorderTop(1).BorderColor(SteelBand).Padding(15).Row(row =>
                {
                    row.RelativeItem().Text("Generated by Cleared").FontSize(8).FontColor(SteelMuted);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(SteelMuted));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });
        });

        // Embedded in the PDF itself rather than left to the client — a blob: URL built
        // from a fetched byte array carries no filename or Content-Disposition of its own,
        // so this is what most browsers fall back to when the Owner saves the file, on
        // both the inline view and a direct download.
        return document
            .WithMetadata(new DocumentMetadata { Title = invoice.Number ?? title })
            .GeneratePdf();
    }

    private static void MetaRow(ColumnDescriptor details, string label, string value) =>
        details.Item().Row(r =>
        {
            r.RelativeItem().Text(label).FontColor(SteelMuted);
            r.RelativeItem().AlignRight().Text(value).Bold();
        });

    private static void HeaderCell(TableCellDescriptor header, string text, bool alignRight = true)
    {
        var cell = header.Cell().Background(SteelBand).PaddingVertical(6).PaddingHorizontal(4);
        var styled = (alignRight ? cell.AlignRight() : cell).Text(text).FontSize(9).Bold().FontColor(SteelText);
    }

    private static void BodyCell(
        TableDescriptor table, string background, string text, bool alignRight = true, bool bold = false)
    {
        var cell = table.Cell().Background(background).BorderBottom(1).BorderColor(SteelBorder)
            .PaddingVertical(6).PaddingHorizontal(4);
        var aligned = alignRight ? cell.AlignRight() : cell;
        var styledText = aligned.Text(text);
        if (bold)
        {
            styledText.Bold();
        }
    }
}
