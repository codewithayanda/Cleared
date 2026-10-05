using System.Text.Json.Nodes;
using Cleared.Application.CreditNotes;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Payments;

namespace Cleared.Integration.Tests;

// Tenant A, the owner, holds a customer, a draft invoice, an issued invoice with a payment and a
// credit note. Tenant B, the intruder, has nothing, so any of A's data that reaches B is a leak.
public sealed class IsolationWorld
{
    public required TestTenant Owner { get; init; }
    public required TestTenant Intruder { get; init; }
    public required CustomerResponse Customer { get; init; }
    public required InvoiceResponse DraftInvoice { get; init; }
    public required InvoiceResponse IssuedInvoice { get; init; }
    public required PaymentResponse Payment { get; init; }
    public required CreditNoteResponse CreditNote { get; init; }

    // Values that identify the owner's data. None may appear in anything sent to the intruder.
    public IReadOnlyList<string> OwnerSecrets =>
    [
        Owner.TenantId.ToString(),
        Customer.Id.ToString(),
        Customer.Name,
        DraftInvoice.Id.ToString(),
        IssuedInvoice.Id.ToString(),
        IssuedInvoice.Lines[0].Id.ToString(),
        Payment.Id.ToString(),
        CreditNote.Id.ToString(),
    ];

    public static async Task<IsolationWorld> CreateAsync(ClearedApiFactory factory)
    {
        var owner = await factory.CreateTenantAsync();
        var intruder = await factory.CreateTenantAsync();

        var customer = await owner.Client.CreateCustomerAsync("Acme Holdings (Owner)");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", TestData.NewInvoice(customer.Id));
        var issued = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);
        var payment = await owner.Client.PostJsonAsync<PaymentResponse>(
            $"api/v1/invoices/{issued.Id}/payments", new RecordPaymentRequest("100.00", TestData.Today, "EFT-1"));
        var creditNote = await owner.Client.PostJsonAsync<CreditNoteResponse>(
            $"api/v1/invoices/{issued.Id}/credit-notes",
            new CreateCreditNoteRequest("Damaged goods", [new CreateCreditNoteLineRequest(issued.Lines[0].Id, 1)]));

        return new IsolationWorld
        {
            Owner = owner,
            Intruder = intruder,
            Customer = customer,
            DraftInvoice = draft,
            IssuedInvoice = issued,
            Payment = payment,
            CreditNote = creditNote,
        };
    }

    // Everything the owner can read, in one string. Taken before and after an attack, equal
    // snapshots prove the attack changed nothing.
    public async Task<string> SnapshotOwnerDataAsync()
    {
        string[] paths = ["customers", "invoices", $"invoices/{IssuedInvoice.Id}/payments", "audit", "tenants/me"];
        var parts = new List<string>();

        foreach (var path in paths)
        {
            var json = await Owner.Client.GetStringAsync($"api/v1/{path}");
            parts.Add($"{path}\n{Canonical(JsonNode.Parse(json))}");
        }

        return string.Join("\n\n", parts);
    }

    // Array elements are ordered by their own text, so a list the database returned in a
    // different order still compares equal.
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonArray array => "[" + string.Join(",", array.Select(Canonical).Order(StringComparer.Ordinal)) + "]",
        JsonObject obj => "{" + string.Join(
            ",", obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"\"{p.Key}\":{Canonical(p.Value)}")) + "}",
        null => "null",
        _ => node.ToJsonString(),
    };
}
