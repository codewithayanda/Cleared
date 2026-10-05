using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Cleared.Application.CreditNotes;

namespace Cleared.Integration.Tests;

[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ClearedApiFactory factory)
{
    public static IEnumerable<object[]> Endpoints() => IsolationCases.All.Select(c => new object[] { c.Endpoint });

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Another_tenant_gets_none_of_the_owners_data_and_changes_none_of_it(string endpoint)
    {
        var isolationCase = IsolationCases.All.Single(c => c.Endpoint == endpoint);
        var world = await IsolationWorld.CreateAsync(factory);
        var before = await world.SnapshotOwnerDataAsync();

        using var attack = isolationCase.CreateRequest(world);
        using var blocked = await world.Intruder.Client.SendAsync(attack);
        await AssertBlockedAsync(isolationCase, world, blocked);
        Assert.Equal(before, await world.SnapshotOwnerDataAsync());

        // Control: the identical request works for the owner, so the answer above was about
        // whose data it is and not about a mistake in the request.
        using var control = isolationCase.CreateRequest(world);
        using var allowed = await world.Owner.Client.SendAsync(control);
        await AssertAllowedAsync(isolationCase, allowed);
    }

    [Fact]
    public async Task A_credit_note_cannot_credit_a_line_from_another_tenants_invoice()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var customer = await world.Intruder.Client.CreateCustomerAsync("Intruder Customer");
        var ownInvoice = await world.Intruder.Client.CreateIssuedInvoiceAsync(customer.Id);
        var before = await world.SnapshotOwnerDataAsync();

        var response = await world.Intruder.Client.PostAsJsonAsync(
            $"api/v1/invoices/{ownInvoice.Id}/credit-notes",
            new CreateCreditNoteRequest(
                "attack", [new CreateCreditNoteLineRequest(world.IssuedInvoice.Lines[0].Id, 1)]),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await world.SnapshotOwnerDataAsync());
    }

    [Fact]
    public async Task Another_tenants_customer_is_indistinguishable_from_one_that_does_not_exist()
    {
        var world = await IsolationWorld.CreateAsync(factory);

        var foreign = await world.Intruder.Client.PostAsJsonAsync(
            "api/v1/invoices", TestData.NewInvoice(world.Customer.Id), ApiJson.Options);
        var missing = await world.Intruder.Client.PostAsJsonAsync(
            "api/v1/invoices", TestData.NewInvoice(Guid.NewGuid()), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_token_whose_tenant_claim_was_edited_is_rejected()
    {
        var owner = await factory.CreateTenantAsync();
        var intruder = await factory.CreateTenantAsync();
        var token = intruder.Client.DefaultRequestHeaders.Authorization!.Parameter!;

        var genuine = await intruder.Client.GetAsync("api/v1/customers");
        Assert.Equal(HttpStatusCode.OK, genuine.StatusCode);

        using var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", WithTenantClaim(token, owner.TenantId));
        var forged = await client.GetAsync("api/v1/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    private static string WithTenantClaim(string token, Guid tenantId)
    {
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64Url.DecodeFromChars(parts[1]))!.AsObject();
        payload["tenant_id"] = tenantId.ToString();

        return $"{parts[0]}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.{parts[2]}";
    }

    private static async Task AssertBlockedAsync(
        IsolationCase isolationCase, IsolationWorld world, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var shown = $"{isolationCase.Endpoint} as the intruder returned {(int)response.StatusCode}: {body}";

        var blocked = isolationCase.Intruder switch
        {
            Blocked.NotFound => response.StatusCode == HttpStatusCode.NotFound,
            Blocked.BadRequest => response.StatusCode == HttpStatusCode.BadRequest,
            Blocked.EmptyList => response.StatusCode == HttpStatusCode.OK && body == "[]",
            Blocked.OwnTenantOnly =>
                response.IsSuccessStatusCode && body.Contains(world.Intruder.TenantId.ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(isolationCase)),
        };
        Assert.True(blocked, shown);

        foreach (var secret in world.OwnerSecrets)
        {
            Assert.False(
                body.Contains(secret, StringComparison.OrdinalIgnoreCase), $"{shown} (contains the owner's {secret})");
        }
    }

    private static async Task AssertAllowedAsync(IsolationCase isolationCase, HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            Assert.Fail(
                $"{isolationCase.Endpoint} as the owner returned {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        if (isolationCase.Intruder == Blocked.EmptyList)
        {
            Assert.NotEqual("[]", await response.Content.ReadAsStringAsync());
        }
    }
}
