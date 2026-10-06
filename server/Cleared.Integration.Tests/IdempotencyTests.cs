using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Auditing;
using Cleared.Application.CreditNotes;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Payments;
using Npgsql;

namespace Cleared.Integration.Tests;

// A repeated request must do its work once and get the same answer again. Each case fails
// without the matching piece of IdempotentExecutor, IdempotencyStore or the controller wiring.
[Collection(ApiCollection.Name)]
public class IdempotencyTests(ClearedApiFactory factory)
{
    private const string KeyHeader = "Idempotency-Key";
    private const string ReplayedHeader = "Idempotent-Replayed";
    private const int Callers = 12;

    public static IEnumerable<object[]> MoneyMovingPosts() =>
        IsolationCases.All.Where(c => c.Method == HttpMethod.Post).Select(c => new object[] { c.Endpoint });

    [Fact]
    public async Task Repeating_a_payment_with_the_same_key_records_it_once_and_answers_the_same()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/payments";
        var payment = new RecordPaymentRequest("100.00", TestData.Today, "EFT-1");
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(Post(url, payment, key));
        using var repeat = await owner.Client.SendAsync(Post(url, payment, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.False(WasReplayed(first));
        Assert.True(WasReplayed(repeat));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, repeat.Headers.Location);
        Assert.Single(await PaymentsAsync(owner, invoice));
    }

    [Fact]
    public async Task Repeating_an_issue_returns_the_issued_invoice_instead_of_a_conflict()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Retry Customer");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", TestData.NewInvoice(customer.Id));
        var url = $"api/v1/invoices/{draft.Id}/issue";
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(Post(url, TestData.IssueToday(), key));
        using var repeat = await owner.Client.SendAsync(Post(url, TestData.IssueToday(), key));
        using var newAttempt = await owner.Client.SendAsync(Post(url, TestData.IssueToday(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.True(WasReplayed(repeat));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());

        // A genuinely new attempt is still a conflict, because the invoice is no longer a draft.
        Assert.Equal(HttpStatusCode.Conflict, newAttempt.StatusCode);
    }

    [Fact]
    public async Task Repeating_a_customer_create_makes_one_customer()
    {
        var owner = await factory.CreateTenantAsync();
        var request = new CreateCustomerRequest("Twice Co", null, "ap@twice.test", "1 Main Road");
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(Post("api/v1/customers", request, key));
        using var repeat = await owner.Client.SendAsync(Post("api/v1/customers", request, key));

        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.True(WasReplayed(repeat));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        var customers = await owner.Client.GetFromJsonAsync<List<CustomerResponse>>(
            "api/v1/customers", ApiJson.Options);
        Assert.Single(customers!);
    }

    [Fact]
    public async Task Repeating_an_invoice_create_makes_one_invoice()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Invoice Twice Customer");
        var request = TestData.NewInvoice(customer.Id);
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(Post("api/v1/invoices", request, key));
        using var repeat = await owner.Client.SendAsync(Post("api/v1/invoices", request, key));

        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.True(WasReplayed(repeat));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        var invoices = await owner.Client.GetFromJsonAsync<List<InvoiceResponse>>(
            "api/v1/invoices", ApiJson.Options);
        Assert.Single(invoices!);
    }

    [Fact]
    public async Task Repeating_a_credit_note_makes_one_credit_note()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/credit-notes";
        var request = new CreateCreditNoteRequest(
            "Returned", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 1)]);
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(Post(url, request, key));
        using var repeat = await owner.Client.SendAsync(Post(url, request, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.True(WasReplayed(repeat));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        var audit = await owner.Client.GetFromJsonAsync<List<AuditLogResponse>>("api/v1/audit", ApiJson.Options);
        Assert.Single(audit!, entry => entry.Action == "CreditNoteIssued");
    }

    [Fact]
    public async Task Twelve_identical_payments_sent_at_once_make_one_payment_and_get_one_answer()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/payments";
        var payment = new RecordPaymentRequest("100.00", TestData.Today, null);
        var key = Guid.NewGuid();

        var responses = await ConcurrentCalls.FireAsync(Callers, () => owner.Client.SendAsync(Post(url, payment, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        Assert.Single(responses, r => !WasReplayed(r));
        Assert.Single(await PaymentsAsync(owner, invoice));
    }

    [Fact]
    public async Task Twelve_identical_issues_sent_at_once_issue_the_invoice_once_without_a_numbering_gap()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Race Customer");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", TestData.NewInvoice(customer.Id));
        var url = $"api/v1/invoices/{draft.Id}/issue";
        var key = Guid.NewGuid();

        var responses = await ConcurrentCalls.FireAsync(
            Callers, () => owner.Client.SendAsync(Post(url, TestData.IssueToday(), key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        var issued = await responses[0].Content.ReadFromJsonAsync<InvoiceResponse>(ApiJson.Options);
        Assert.Equal($"INV-{TestData.Today.Year}-0001", issued!.Number);
        var next = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);
        Assert.Equal($"INV-{TestData.Today.Year}-0002", next.Number);
    }

    [Fact]
    public async Task The_same_key_for_a_different_request_is_refused()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/payments";
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(
            Post(url, new RecordPaymentRequest("100.00", TestData.Today, null), key));
        using var different = await owner.Client.SendAsync(
            Post(url, new RecordPaymentRequest("200.00", TestData.Today, null), key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        var payment = Assert.Single(await PaymentsAsync(owner, invoice));
        Assert.Equal("100.00", payment.Amount);
    }

    [Fact]
    public async Task The_same_key_for_a_different_invoice_is_refused()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var other = await owner.Client.CreateIssuedInvoiceAsync(invoice.CustomerId);
        var payment = new RecordPaymentRequest("100.00", TestData.Today, null);
        var key = Guid.NewGuid();

        using var first = await owner.Client.SendAsync(Post($"api/v1/invoices/{invoice.Id}/payments", payment, key));
        using var elsewhere = await owner.Client.SendAsync(Post($"api/v1/invoices/{other.Id}/payments", payment, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, elsewhere.StatusCode);
        Assert.Empty(await PaymentsAsync(owner, other));
    }

    [Fact]
    public async Task Different_keys_are_separate_actions()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/payments";
        var payment = new RecordPaymentRequest("100.00", TestData.Today, null);

        using var first = await owner.Client.SendAsync(Post(url, payment, Guid.NewGuid()));
        using var second = await owner.Client.SendAsync(Post(url, payment, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(WasReplayed(second));
        Assert.Equal(2, (await PaymentsAsync(owner, invoice)).Count);
    }

    [Theory]
    [MemberData(nameof(MoneyMovingPosts))]
    public async Task A_missing_or_malformed_key_is_a_400_on_every_money_moving_post(string endpoint)
    {
        var isolationCase = IsolationCases.All.Single(c => c.Endpoint == endpoint);
        var world = await IsolationWorld.CreateAsync(factory);

        using var missing = isolationCase.CreateRequest(world);
        missing.Headers.Add(IdempotencyKeyHandler.WithoutKeyHeader, "1");
        using var malformed = isolationCase.CreateRequest(world);
        malformed.Headers.Add(KeyHeader, "not-a-guid");

        using var missingResponse = await world.Owner.Client.SendAsync(missing);
        using var malformedResponse = await world.Owner.Client.SendAsync(malformed);

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
    }

    [Fact]
    public async Task A_failed_attempt_leaves_no_trace_so_the_key_can_be_used_again()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/payments";
        var key = Guid.NewGuid();

        // The invoice is R1000, so this overpays and is refused.
        using var tooMuch = await owner.Client.SendAsync(
            Post(url, new RecordPaymentRequest("5000.00", TestData.Today, null), key));
        using var corrected = await owner.Client.SendAsync(
            Post(url, new RecordPaymentRequest("500.00", TestData.Today, null), key));

        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.False(WasReplayed(corrected));
        Assert.Single(await PaymentsAsync(owner, invoice));
    }

    [Fact]
    public async Task A_replay_returns_the_original_answer_even_after_the_invoice_has_moved_on()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var url = $"api/v1/invoices/{invoice.Id}/payments";
        var firstKey = Guid.NewGuid();
        var firstPayment = new RecordPaymentRequest("100.00", TestData.Today, null);

        using var first = await owner.Client.SendAsync(Post(url, firstPayment, firstKey));
        using var later = await owner.Client.SendAsync(
            Post(url, new RecordPaymentRequest("200.00", TestData.Today, null), Guid.NewGuid()));
        using var replay = await owner.Client.SendAsync(Post(url, firstPayment, firstKey));

        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
        Assert.True(WasReplayed(replay));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(2, (await PaymentsAsync(owner, invoice)).Count);
    }

    [Fact]
    public async Task The_same_key_in_two_tenants_never_replays_across_them()
    {
        var alpha = await factory.CreateTenantAsync();
        var beta = await factory.CreateTenantAsync();
        var key = Guid.NewGuid();

        using var forAlpha = await alpha.Client.SendAsync(
            Post("api/v1/customers", new CreateCustomerRequest("Alpha Co", null, null, null), key));
        using var forBeta = await beta.Client.SendAsync(
            Post("api/v1/customers", new CreateCustomerRequest("Beta Co", null, null, null), key));

        Assert.Equal(HttpStatusCode.Created, forAlpha.StatusCode);
        Assert.Equal(HttpStatusCode.Created, forBeta.StatusCode);
        Assert.False(WasReplayed(forBeta));
        var betaBody = await forBeta.Content.ReadAsStringAsync();
        Assert.Contains("Beta Co", betaBody);
        Assert.DoesNotContain("Alpha Co", betaBody);
        var alphaCustomers = await alpha.Client.GetFromJsonAsync<List<CustomerResponse>>(
            "api/v1/customers", ApiJson.Options);
        var betaCustomers = await beta.Client.GetFromJsonAsync<List<CustomerResponse>>(
            "api/v1/customers", ApiJson.Options);
        Assert.Equal("Alpha Co", Assert.Single(alphaCustomers!).Name);
        Assert.Equal("Beta Co", Assert.Single(betaCustomers!).Name);
    }

    [Fact]
    public async Task Keys_older_than_a_day_are_purged_by_the_tenants_next_keyed_request()
    {
        var owner = await factory.CreateTenantAsync();
        var oldKey = Guid.NewGuid();
        var recentKey = Guid.NewGuid();
        await InsertKeyAsync(owner.TenantId, oldKey, DateTimeOffset.UtcNow.AddHours(-25));
        await InsertKeyAsync(owner.TenantId, recentKey, DateTimeOffset.UtcNow.AddHours(-1));

        using var response = await owner.Client.SendAsync(
            Post("api/v1/customers", new CreateCustomerRequest("Purge Co", null, null, null), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(await KeyExistsAsync(owner.TenantId, oldKey));
        Assert.True(await KeyExistsAsync(owner.TenantId, recentKey));
    }

    private async Task<(TestTenant Owner, InvoiceResponse Invoice)> IssuedInvoiceAsync()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Retry Customer");

        return (owner, await owner.Client.CreateIssuedInvoiceAsync(customer.Id));
    }

    private static HttpRequestMessage Post(string url, object body, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, body.GetType(), options: ApiJson.Options),
        };
        request.Headers.Add(KeyHeader, key.ToString());

        return request;
    }

    private static bool WasReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(ReplayedHeader, out var values) && values.Contains("true");

    private static async Task<List<PaymentResponse>> PaymentsAsync(TestTenant owner, InvoiceResponse invoice) =>
        (await owner.Client.GetFromJsonAsync<List<PaymentResponse>>(
            $"api/v1/invoices/{invoice.Id}/payments", ApiJson.Options))!;

    private async Task InsertKeyAsync(Guid tenantId, Guid key, DateTimeOffset createdAt)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO idempotency_keys (tenant_id, idempotency_key, fingerprint, response, created_at) " +
            "VALUES (@tenantId, @key, 'test', '{}', @createdAt)",
            connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("createdAt", createdAt);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> KeyExistsAsync(Guid tenantId, Guid key)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM idempotency_keys WHERE tenant_id = @tenantId AND idempotency_key = @key",
            connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("key", key);

        return (long)(await command.ExecuteScalarAsync())! == 1;
    }
}
