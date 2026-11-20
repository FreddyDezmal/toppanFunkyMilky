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
/// M8b patron cancellation against a simulated Paystack: free cancellation of unpaid orders (B1.3), an open checkout
/// settled first (R6), the 10% fee on paid orders (B1.4, R5), the order and refund saved together before Paystack is
/// asked (R4), one refund however many requests arrive, the pickup-time limit (B1.1) and access control.
/// Every test order costs 12.00 + 6.00 + 8.00 = 26.00 + VAT 3.90 = 29.90; fee 10% × 26.00 = 2.60; refund 27.30.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderCancellationTests(PostgresApiFactory factory) : IDisposable
{
    private readonly FakePaymentGateway _gateway = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(_gateway));
    });

    public void Dispose() => _app?.Dispose();

    // ---- unpaid orders ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUnpaidOrder_IsCancelledForFree_AndARepeatIsHarmless()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using (patron)
        {
            CancellationResponse quote = await GetAsync(patron, order);
            Assert.Equal((true, false, 0m, 0m), (quote.CanCancel, quote.IsPaid, quote.Fee, quote.RefundAmount));

            using HttpResponseMessage cancelled = await CancelAsync(patron, order, 0m);
            using HttpResponseMessage again = await CancelAsync(patron, order, 0m);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (cancelled.StatusCode, again.StatusCode));
            CancellationResponse body = await cancelled.ReadJsonAsync<CancellationResponse>();
            Assert.Equal(("Cancelled", false, "AlreadyCancelled"), (body.OrderStatus, body.CanCancel, body.CannotCancelReason));
            Assert.Empty(body.Refunds);
            Assert.Equal(OrderStatus.Cancelled, await OrderStatusAsync(order));
            Assert.Empty(_gateway.RefundRequests);
        }
    }

    [Fact]
    public async Task AnOpenCheckout_IsClosedFirst_AndTheOrderCanNoLongerBePaid()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using (patron)
        {
            string payment = await StartPaymentAsync(patron, order);

            using HttpResponseMessage cancelled = await CancelAsync(patron, order, 0m);
            using HttpResponseMessage payAfterwards = await StartPaymentResponseAsync(patron, order);

            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal(1, _gateway.VerifyCalls);
            Assert.Equal((PaymentStatus.Superseded, OrderStatus.Cancelled), await StateAsync(order, payment));
            Assert.Equal(HttpStatusCode.Conflict, payAfterwards.StatusCode);
        }
    }

    [Fact]
    public async Task ACheckoutPaidJustBeforeCancelling_IsKept_AndThePatronSeesTheRefundBeforeAnythingIsCancelled()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using (patron)
        {
            string payment = await StartPaymentAsync(patron, order);
            Answer(payment, FakePaymentGateway.Paid(2990));

            using HttpResponseMessage changed = await CancelAsync(patron, order, 0m);

            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            using (JsonDocument problem = JsonDocument.Parse(await changed.Content.ReadAsStringAsync()))
            {
                JsonElement figures = problem.RootElement.GetProperty("cancellation");
                Assert.True(figures.GetProperty("isPaid").GetBoolean());
                Assert.Equal(27.30m, figures.GetProperty("refundAmount").GetDecimal());
            }

            Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
            Assert.Empty(_gateway.RefundRequests);

            using HttpResponseMessage confirmed = await CancelAsync(patron, order, 27.30m);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            Assert.Equal(OrderStatus.Cancelled, await OrderStatusAsync(order));
            Assert.Single(_gateway.RefundRequests);
        }
    }

    // ---- paid orders -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOrderHistory_FollowsAPaidOrderThroughCancellationAndItsRefund()
    {
        (HttpClient patron, string order, _) = await PaidOrderAsync();
        using (patron)
        {
            Assert.Equal(("Paid", (string?)null), await HistoryStatusAsync(patron, order));

            using HttpResponseMessage cancelled = await CancelAsync(patron, order, 27.30m);
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal(("Cancelled", (string?)"RefundInProgress"), await HistoryStatusAsync(patron, order));

            // Paystack confirms the refund (simulated directly; the confirmation path itself is covered by the M8b tests).
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                MilkshakeDbContext db = Db(scope);
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE milkshake.refunds SET status = 'Processed', completed_at = now() WHERE order_id = (SELECT id FROM milkshake.orders WHERE reference = {order})");
            }

            Assert.Equal(("Cancelled", (string?)"RefundedLessFee"), await HistoryStatusAsync(patron, order));
        }
    }

    private static async Task<(string Payment, string? Refund)> HistoryStatusAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage response = await patron.GetAsync(new Uri("/api/ordering/orders", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        OrderSummaryResponse row = Assert.Single((await response.ReadJsonAsync<OrderHistoryResponse>()).Items, o => o.Reference == order);
        return (row.PaymentStatus, row.RefundStatus);
    }

    [Fact]
    public async Task APaidOrder_ShowsTheFee_ThenIsCancelledWithItsRefundSentToPaystack()
    {
        (HttpClient patron, string order, string payment) = await PaidOrderAsync();
        using (patron)
        {
            CancellationResponse quote = await GetAsync(patron, order);
            Assert.Equal((true, true, 29.90m, 0.10m, 2.60m, 27.30m, "ZAR"),
                (quote.CanCancel, quote.IsPaid, quote.PaidAmount, quote.FeeRate, quote.Fee, quote.RefundAmount, quote.CurrencyCode));

            using HttpResponseMessage response = await CancelAsync(patron, order, 27.30m);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            RefundResponse refund = Assert.Single((await response.ReadJsonAsync<CancellationResponse>()).Refunds);
            Assert.Equal(("PatronCancellation", "Pending", 27.30m, 2.60m, false), (refund.Kind, refund.Status, refund.Amount, refund.Fee, refund.NeedsAttention));
            Assert.Null(refund.CompletedAt);
            Assert.Matches(Refund.ReferencePattern, refund.Reference);

            RefundRequest sent = Assert.Single(_gateway.RefundRequests);
            Assert.Equal((refund.Reference, payment, 2730L, "ZAR"), (sent.Reference, sent.TransactionReference, sent.AmountMinor, sent.CurrencyCode));

            Refund stored = Assert.Single(await RefundsAsync(order));
            Assert.Equal((RefundStatus.Pending, 2990L, 260L, 2730L), (stored.Status, stored.PaidAmountMinor, stored.FeeMinor, stored.AmountMinor));
            Assert.NotNull(stored.ProviderRefundId);
            Assert.Equal(OrderStatus.Cancelled, await OrderStatusAsync(order));
        }
    }

    [Theory]
    [InlineData("29.90")]
    [InlineData("0")]
    [InlineData("27.31")]
    public async Task ARefundOtherThanTheServers_CancelsNothing(string expected)
    {
        (HttpClient patron, string order, _) = await PaidOrderAsync();
        using (patron)
        {
            using HttpResponseMessage response = await CancelAsync(patron, order, decimal.Parse(expected, CultureInfo.InvariantCulture));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(27.30m, problem.RootElement.GetProperty("cancellation").GetProperty("refundAmount").GetDecimal());
            Assert.Equal(OrderStatus.Confirmed, await OrderStatusAsync(order));
            Assert.Empty(await RefundsAsync(order));
            Assert.Empty(_gateway.RefundRequests);
        }
    }

    [Fact]
    public async Task WhenPaystackCannotTakeTheRefundNow_TheOrderIsStillCancelled_AndTheRefundWaitsForRetry()
    {
        (HttpClient patron, string order, _) = await PaidOrderAsync();
        using (patron)
        {
            _gateway.OnCreateRefund = _ => throw new PaymentGatewayException("timeout");

            using HttpResponseMessage response = await CancelAsync(patron, order, 27.30m);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Initiating", Assert.Single((await response.ReadJsonAsync<CancellationResponse>()).Refunds).Status);
            Assert.Equal(RefundStatus.Initiating, Assert.Single(await RefundsAsync(order)).Status);
            Assert.Equal(OrderStatus.Cancelled, await OrderStatusAsync(order));
        }
    }

    [Fact]
    public async Task SimultaneousCancellations_CancelOnce_AndRefundOnce()
    {
        (HttpClient patron, string order, _) = await PaidOrderAsync();
        using (patron)
        {
            string csrf = await patron.GetCsrfTokenAsync();

            HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CancelAsync(patron, order, 27.30m, csrf)));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Assert.Single(await RefundsAsync(order));
            Assert.Single(_gateway.RefundRequests);
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task APendingManagerRequest_LapsesWhenThePatronCancels()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (patron)
        {
            int requestId = await AddManagerRequestAsync(order, manager.Email);

            using HttpResponseMessage response = await CancelAsync(patron, order, 0m);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            CancellationRequest request = await Db(scope).CancellationRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
            Assert.Equal(CancellationRequestStatus.Lapsed, request.Status);
        }
    }

    [Fact]
    public async Task OnceThePickupTimeHasPassed_NothingCanBeCancelled()
    {
        (HttpClient patron, string order, _) = await PaidOrderAsync();
        using (patron)
        {
            await ExecuteAsync("UPDATE milkshake.orders SET pickup_at_utc = now() - interval '1 minute' WHERE reference = {0}", order);

            CancellationResponse quote = await GetAsync(patron, order);
            using HttpResponseMessage response = await CancelAsync(patron, order, 27.30m);

            Assert.Equal((false, "PickupPassed"), (quote.CanCancel, quote.CannotCancelReason));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal(OrderStatus.Confirmed, await OrderStatusAsync(order));
            Assert.Empty(_gateway.RefundRequests);
        }
    }

    // ---- access and request shape ----------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheOrdersPatron_CanSeeOrCancelIt()
    {
        (HttpClient owner, string order) = await PatronWithOrderAsync();
        using HttpClient other = await RegisteredPatronAsync();
        using HttpClient anonymous = App.CreateSecureClient();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (owner)
        {
            using HttpResponseMessage otherGet = await other.GetAsync(CancellationUri(order));
            using HttpResponseMessage otherCancel = await CancelAsync(other, order, 0m);
            using HttpResponseMessage anonymousCancel = await CancelAsync(anonymous, order, 0m);
            using HttpResponseMessage managerCancel = await CancelAsync(manager.Client, order, 0m);
            using HttpResponseMessage withoutCsrf = await SendCancelAsync(owner, order, "{\"expectedRefund\":0}", csrfToken: null);

            Assert.Equal(
                (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.BadRequest),
                (otherGet.StatusCode, otherCancel.StatusCode, anonymousCancel.StatusCode, managerCancel.StatusCode, withoutCsrf.StatusCode));
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"expectedRefund\":-1}")]
    [InlineData("{\"expectedRefund\":0,\"refundAmount\":29.90}")]
    [InlineData("{\"expectedRefund\":0,\"feeRate\":0}")]
    [InlineData("{\"expectedRefund\":0,\"status\":\"Cancelled\"}")]
    public async Task MissingOrForgedFields_AreRejected(string json)
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage response = await SendCancelAsync(patron, order, json, await patron.GetCsrfTokenAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order));
        }
    }

    [Fact]
    public async Task AnOversizedCancelRequest_IsRefused()
    {
        using HttpClient patron = await RegisteredPatronAsync();

        using HttpResponseMessage response = await SendCancelAsync(patron, "MS-0000-0000", "{\"expectedRefund\":0}" + new string(' ', 4_000),
            await patron.GetCsrfTokenAsync());

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

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

    private async Task<int> AddManagerRequestAsync(string order, string managerEmail)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order stored = await db.Orders.AsNoTracking().SingleAsync(o => o.Reference == order);
        Guid managerId = await db.Users.Where(u => u.Email == managerEmail).Select(u => u.Id).SingleAsync();
        var request = CancellationRequest.Create(stored, managerId, "Out of milk", DateTimeOffset.UtcNow);
        db.CancellationRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
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

    private async Task<(PaymentStatus Payment, OrderStatus Order)> StateAsync(string order, string payment)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        return (
            await db.Payments.Where(p => p.Reference == payment).Select(p => p.Status).SingleAsync(),
            await db.Orders.Where(o => o.Reference == order).Select(o => o.Status).SingleAsync());
    }

    private async Task ExecuteAsync(string sql, string value)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql, value);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
