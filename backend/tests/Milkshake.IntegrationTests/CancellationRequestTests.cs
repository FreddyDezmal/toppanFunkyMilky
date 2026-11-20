using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8b manager cancellation requests (B1.2, Q1, Q3, Q4): a manager finds an order and asks; nothing changes until the
/// patron accepts (full refund if paid) or declines; one pending request per order; requests close at the pickup time;
/// refunds needing manual handling are listed for managers; access control on both sides.
/// Every test order costs 29.90.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class CancellationRequestTests(PostgresApiFactory factory) : IDisposable
{
    private readonly FakePaymentGateway _gateway = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(_gateway));
    });

    public void Dispose() => _app?.Dispose();

    [Fact]
    public async Task AManager_FindsAnOrderByItsReference_AsTyped_WithThePatronsContact()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            using HttpResponseMessage found = await manager.Client.GetAsync(ManagerOrderUri(" " + order.ToLowerInvariant() + " "));
            using HttpResponseMessage missing = await manager.Client.GetAsync(ManagerOrderUri("MS-0000-0000"));

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (found.StatusCode, missing.StatusCode));
            ManagerOrderResponse body = await found.ReadJsonAsync<ManagerOrderResponse>();
            Assert.Equal((order, "PendingPayment", false, 29.90m, true), (body.Reference, body.Status, body.IsPaid, body.Total, body.CanRequestCancellation));
            Assert.Contains("@example.test", body.PatronEmail, StringComparison.Ordinal);
            Assert.Empty(body.Requests);
        }
    }

    [Fact]
    public async Task ARequest_WaitsForThePatron_WithoutChangingTheOrder_AndOnlyOneCanWait()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            using HttpResponseMessage requested = await RequestAsync(manager.Client, order, "  Out of milk  ");
            using HttpResponseMessage again = await RequestAsync(manager.Client, order, "Machine broken");

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Conflict), (requested.StatusCode, again.StatusCode));
            ManagerOrderResponse body = await requested.ReadJsonAsync<ManagerOrderResponse>();
            CancellationRequestResponse request = Assert.Single(body.Requests);
            Assert.Equal(("Out of milk", "Pending", true, false), (request.Reason, request.Status, request.IsOpen, body.CanRequestCancellation));
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order));

            CancellationResponse seen = await GetAsync(patron, order);
            Assert.Equal(("Out of milk", true, 0m), (seen.OpenRequest!.Reason, seen.OpenRequest.IsOpen, seen.OpenRequest.RefundAmount));
            List<PendingCancellationRequestResponse> notices = await NoticesAsync(patron);
            Assert.Equal(order, Assert.Single(notices).OrderReference);
        }
    }

    [Theory]
    [InlineData("{\"reason\":\"\"}")]
    [InlineData("{\"reason\":\"   \"}")]
    [InlineData("{\"reason\":\"This reason is far too long to fit in fifty chars!!\"}")]
    [InlineData("{\"reason\":\"two\\nlines\"}")]
    [InlineData("{}")]
    [InlineData("{\"reason\":\"Out of milk\",\"cancelNow\":true}")]
    public async Task AReasonIsRequired_ShortAndPlain(string json)
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            using HttpResponseMessage response = await SendAsync(manager.Client, ManagerRequestPath(order), json, await manager.Client.GetCsrfTokenAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null((await GetAsync(patron, order)).OpenRequest);
        }
    }

    [Fact]
    public async Task CancelledOrders_AndOrdersPastTheirPickupTime_CannotBeRequested()
    {
        (HttpClient patron, string cancelled) = await PatronWithOrderAsync();
        (HttpClient other, string late) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        using (other)
        {
            using (HttpResponseMessage cancel = await CancelAsync(patron, cancelled, 0m))
            {
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            }

            await ExecuteAsync("UPDATE milkshake.orders SET pickup_at_utc = now() - interval '1 minute' WHERE reference = {0}", late);

            using HttpResponseMessage first = await RequestAsync(manager.Client, cancelled, "Out of milk");
            using HttpResponseMessage second = await RequestAsync(manager.Client, late, "Out of milk");

            Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (first.StatusCode, second.StatusCode));
        }
    }

    [Fact]
    public async Task AcceptingForAnUnpaidOrder_CancelsItForFree()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            (await RequestAsync(manager.Client, order, "Out of milk")).Dispose();

            using HttpResponseMessage accepted = await AnswerAsync(patron, order, "accept", "{\"expectedRefund\":0}");

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(OrderStatus.Cancelled, await OrderStatusAsync(order));
            Assert.Equal(CancellationRequestStatus.Accepted, await RequestStatusAsync(order));
            Assert.Empty(await RefundsAsync(order));
            Assert.Empty(await NoticesAsync(patron));
        }
    }

    [Fact]
    public async Task AcceptingForAPaidOrder_RefundsEverythingPaid_WithoutAFee()
    {
        (HttpClient patron, string order, string payment) = await PaidOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            (await RequestAsync(manager.Client, order, "Out of milk")).Dispose();
            Assert.Equal(29.90m, (await GetAsync(patron, order)).OpenRequest!.RefundAmount);

            using HttpResponseMessage withFee = await AnswerAsync(patron, order, "accept", "{\"expectedRefund\":27.30}");
            Assert.Equal(HttpStatusCode.Conflict, withFee.StatusCode);
            Assert.Equal(OrderStatus.Confirmed, await OrderStatusAsync(order));

            using HttpResponseMessage accepted = await AnswerAsync(patron, order, "accept", "{\"expectedRefund\":29.90}");

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            RefundResponse refund = Assert.Single((await accepted.ReadJsonAsync<CancellationResponse>()).Refunds);
            Assert.Equal(("RestaurantCancellation", 29.90m, 0m, "Pending"), (refund.Kind, refund.Amount, refund.Fee, refund.Status));
            RefundRequest sent = Assert.Single(_gateway.RefundRequests);
            Assert.Equal((payment, 2990L), (sent.TransactionReference, sent.AmountMinor));
            Assert.Equal((OrderStatus.Cancelled, CancellationRequestStatus.Accepted), (await OrderStatusAsync(order), await RequestStatusAsync(order)));
        }
    }

    [Fact]
    public async Task Declining_LeavesTheOrderAsItWas_AndTheManagerMayAskAgain()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            (await RequestAsync(manager.Client, order, "Out of milk")).Dispose();

            using HttpResponseMessage declined = await AnswerAsync(patron, order, "decline", "{}");
            using HttpResponseMessage declinedAgain = await AnswerAsync(patron, order, "decline", "");
            using HttpResponseMessage acceptAfterwards = await AnswerAsync(patron, order, "accept", "{\"expectedRefund\":0}");

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Conflict),
                (declined.StatusCode, declinedAgain.StatusCode, acceptAfterwards.StatusCode));
            Assert.Equal((OrderStatus.PendingPayment, CancellationRequestStatus.Declined), (await OrderStatusAsync(order), await RequestStatusAsync(order)));

            using HttpResponseMessage askedAgain = await RequestAsync(manager.Client, order, "Machine broken");
            Assert.Equal(HttpStatusCode.Created, askedAgain.StatusCode);
            Assert.Equal(2, (await askedAgain.ReadJsonAsync<ManagerOrderResponse>()).Requests.Count);
        }
    }

    [Fact]
    public async Task AfterThePickupTime_ARequestCanNoLongerBeAnswered()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (patron)
        {
            (await RequestAsync(manager.Client, order, "Out of milk")).Dispose();
            await ExecuteAsync("UPDATE milkshake.orders SET pickup_at_utc = now() - interval '1 minute' WHERE reference = {0}", order);

            using HttpResponseMessage accepted = await AnswerAsync(patron, order, "accept", "{\"expectedRefund\":0}");
            using HttpResponseMessage declined = await AnswerAsync(patron, order, "decline", "{}");

            Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (accepted.StatusCode, declined.StatusCode));
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order));
            Assert.Null((await GetAsync(patron, order)).OpenRequest);
            Assert.Empty(await NoticesAsync(patron));
        }
    }

    [Fact]
    public async Task OnlyManagersRequest_AndOnlyTheOrdersPatronAnswers()
    {
        (HttpClient owner, string order) = await PatronWithOrderAsync();
        using HttpClient other = await RegisteredPatronAsync();
        using HttpClient anonymous = App.CreateSecureClient();
        using ManagerSession manager = await ManagerAsync();
        using (owner)
        {
            using HttpResponseMessage patronRequests = await RequestAsync(owner, order, "Out of milk");
            using HttpResponseMessage patronReads = await owner.GetAsync(ManagerOrderUri(order));
            using HttpResponseMessage anonymousRequests = await RequestAsync(anonymous, order, "Out of milk");
            using HttpResponseMessage withoutCsrf = await SendAsync(manager.Client, ManagerRequestPath(order), "{\"reason\":\"Out of milk\"}", csrfToken: null);
            Assert.Equal(
                (HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest),
                (patronRequests.StatusCode, patronReads.StatusCode, anonymousRequests.StatusCode, withoutCsrf.StatusCode));

            (await RequestAsync(manager.Client, order, "Out of milk")).Dispose();
            using HttpResponseMessage otherAccepts = await AnswerAsync(other, order, "accept", "{\"expectedRefund\":0}");
            using HttpResponseMessage managerAccepts = await AnswerAsync(manager.Client, order, "accept", "{\"expectedRefund\":0}");

            Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Forbidden), (otherAccepts.StatusCode, managerAccepts.StatusCode));
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order));
            Assert.Empty(await NoticesAsync(other));
        }
    }

    [Fact]
    public async Task RefundsThatFailOrNeedAttention_AreListedForManagers()
    {
        (HttpClient first, string failedOrder, _) = await PaidOrderAsync();
        (HttpClient second, string attentionOrder, _) = await PaidOrderAsync();
        using ManagerSession manager = await ManagerAsync();
        using (first)
        using (second)
        {
            _gateway.OnCreateRefund = request => new GatewayRefund(true, VerifiedRefundOutcome.Failed, request.AmountMinor, FakePaymentGateway.NextRefundId(), "Refund failed at Paystack.");
            (await CancelAsync(first, failedOrder, 27.30m)).Dispose();
            _gateway.OnCreateRefund = request => new GatewayRefund(true, VerifiedRefundOutcome.Pending, request.AmountMinor, FakePaymentGateway.NextRefundId(), GatewayRefund.NeedsAttentionMessage);
            (await CancelAsync(second, attentionOrder, 27.30m)).Dispose();

            using HttpResponseMessage response = await manager.Client.GetAsync(new Uri("/api/manager/refunds/attention", UriKind.Relative));
            using HttpResponseMessage asPatron = await first.GetAsync(new Uri("/api/manager/refunds/attention", UriKind.Relative));

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden), (response.StatusCode, asPatron.StatusCode));
            List<RefundNeedingAttentionResponse> listed = await response.ReadJsonAsync<List<RefundNeedingAttentionResponse>>();
            Assert.Equal(("Failed", 27.30m), (listed.Single(r => r.OrderReference == failedOrder).Status, listed.Single(r => r.OrderReference == failedOrder).Amount));
            Assert.Equal("Pending", listed.Single(r => r.OrderReference == attentionOrder).Status);
            Assert.True((await GetAsync(second, attentionOrder)).Refunds.Single().NeedsAttention);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private Task<ManagerSession> ManagerAsync() => factory.CreateManagerSessionAsync();

    private static Uri ManagerOrderUri(string order) => new($"/api/manager/orders/{Uri.EscapeDataString(order)}", UriKind.Relative);

    private static string ManagerRequestPath(string order) => $"/api/manager/orders/{order}/cancellation-requests";

    private static async Task<HttpResponseMessage> RequestAsync(HttpClient client, string order, string reason) =>
        await SendAsync(client, ManagerRequestPath(order), JsonSerializer.Serialize(new { reason }), await client.GetCsrfTokenAsync());

    private static async Task<HttpResponseMessage> AnswerAsync(HttpClient client, string order, string answer, string json) =>
        await SendAsync(client, $"/api/ordering/orders/{order}/cancellation-request/{answer}", json, await client.GetCsrfTokenAsync());

    private static async Task<List<PendingCancellationRequestResponse>> NoticesAsync(HttpClient patron)
    {
        using HttpResponseMessage response = await patron.GetAsync(new Uri("/api/ordering/cancellation-requests", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<List<PendingCancellationRequestResponse>>();
    }

    private async Task<CancellationRequestStatus> RequestStatusAsync(string order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        return await db.CancellationRequests.Where(r => db.Orders.Any(o => o.Id == r.OrderId && o.Reference == order))
            .OrderByDescending(r => r.Id).Select(r => r.Status).FirstAsync();
    }

    /// <summary>The simulated provider answers for this payment; every other open attempt in the shared database stays open.</summary>
    private void Answer(string paymentReference, GatewayVerification result) =>
        _gateway.OnVerify = reference => reference == paymentReference ? result : new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);

    private async Task<(HttpClient Patron, string Order, string Payment)> PaidOrderAsync()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        string payment = await StartPaymentAsync(patron, order);
        Answer(payment, FakePaymentGateway.Paid(2990));
        using HttpResponseMessage refreshed = await SendAsync(patron, $"/api/ordering/orders/{order}/payments/refresh", "{}", await patron.GetCsrfTokenAsync());
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(OrderStatus.Confirmed, await OrderStatusAsync(order));
        _gateway.OnVerify = _ => new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);
        return (patron, order, payment);
    }

    private async Task<string> StartPaymentAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage started = await StartPaymentResponseAsync(patron, order);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        return (await started.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference;
    }

    private async Task<HttpResponseMessage> StartPaymentResponseAsync(HttpClient patron, string order) =>
        await SendAsync(patron, $"/api/ordering/orders/{order}/payments", "{}", await patron.GetCsrfTokenAsync(), Guid.NewGuid().ToString());

    private async Task<(HttpClient Patron, string Order)> PatronWithOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        HttpClient patron = await RegisteredPatronAsync();
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        string draft = JsonSerializer.Serialize(new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        });
        using HttpResponseMessage placed = await SendAsync(patron, "/api/ordering/orders", draft, await patron.GetCsrfTokenAsync(), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (patron, (await placed.ReadJsonAsync<OrderResponse>()).Reference);
    }

    private async Task<HttpClient> RegisteredPatronAsync()
    {
        HttpClient client = App.CreateSecureClient();
        using HttpResponseMessage registered = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return client;
    }

    private static Uri CancellationUri(string order) => new($"/api/ordering/orders/{order}/cancellation", UriKind.Relative);

    private static async Task<CancellationResponse> GetAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage response = await patron.GetAsync(CancellationUri(order));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CancellationResponse>();
    }

    private static async Task<HttpResponseMessage> CancelAsync(HttpClient client, string order, decimal expectedRefund, string? csrfToken = null) =>
        await SendCancelAsync(client, order,
            "{\"expectedRefund\":" + expectedRefund.ToString(CultureInfo.InvariantCulture) + "}",
            csrfToken ?? await client.GetCsrfTokenAsync());

    private static Task<HttpResponseMessage> SendCancelAsync(HttpClient client, string order, string json, string? csrfToken) =>
        SendAsync(client, $"/api/ordering/orders/{order}/cancel", json, csrfToken);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string json, string? csrfToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (csrfToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add(OrderMessages.IdempotencyKeyHeader, idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private async Task<List<Refund>> RefundsAsync(string order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        return await db.Refunds.AsNoTracking().Where(r => db.Orders.Any(o => o.Id == r.OrderId && o.Reference == order)).ToListAsync();
    }

    private async Task<OrderStatus> OrderStatusAsync(string order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).Orders.Where(o => o.Reference == order).Select(o => o.Status).SingleAsync();
    }

    private async Task ExecuteAsync(string sql, string value)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql, value);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
