using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>
/// Frequent-customer discounts switched on (decisions Q-01 to Q-04, 22 Nov 2026): only paid, not-cancelled orders
/// count, and an active tier takes its rate off the whole order before VAT, capped per order. Every test that changes
/// the tiers puts them back as it found them, so the rest of the suite still sees inactive tiers.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DiscountEligibilityTests(PostgresApiFactory factory)
{
    private const string QuotePath = "/api/ordering/quote";
    private const string TiersPath = "/api/manager/discount-tiers";

    [Fact]
    public async Task History_CountsOnlyPaidOrdersThatWereNotCancelled_WithTheirDrinks()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        (HttpClient patronClient, Guid patron) = await RegisterPatronAsync();
        using HttpClient client = patronClient;

        await AddOrderAsync(menu, patron, drinks: 3, OrderOutcome.Paid);
        await AddOrderAsync(menu, patron, drinks: 2, OrderOutcome.Paid);
        await AddOrderAsync(menu, patron, drinks: 4, OrderOutcome.PaidThenCancelled);
        await AddOrderAsync(menu, patron, drinks: 4, OrderOutcome.PaymentFailed);
        await AddOrderAsync(menu, patron, drinks: 4, OrderOutcome.Unpaid);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DiscountHistory history = await scope.ServiceProvider.GetRequiredService<IDiscountEligibilitySource>()
            .GetHistoryAsync(patron, CancellationToken.None);

        Assert.Equal([3, 2], history.PreviousOrderDrinkCounts);
    }

    [Fact]
    public async Task History_OfAPatronWithoutOrders_IsEmpty()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        DiscountHistory history = await scope.ServiceProvider.GetRequiredService<IDiscountEligibilitySource>()
            .GetHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(history.PreviousOrderDrinkCounts);
    }

    [Fact]
    public async Task AnActivatedTier_TakesItsRateOffBeforeVat()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        (HttpClient patronClient, Guid patron) = await RegisterPatronAsync();
        using HttpClient client = patronClient;
        await AddOrderAsync(menu, patron, drinks: 2, OrderOutcome.Paid);
        await AddOrderAsync(menu, patron, drinks: 3, OrderOutcome.Paid);

        await WithOnlyTierOneActiveAsync(manager, minPreviousOrders: 2, minDrinksPerOrder: 2, rate: 0.05m, async () =>
        {
            OrderTotalsResponse totals = await QuoteAsync(client, menu, drinks: 4);

            // 4 × 26.00 = 104.00; 5% = 5.20; taxable 98.80; VAT 15% = 14.82; total 113.62.
            Assert.Equal((1, (decimal?)0.05m, 5.20m, false), (totals.Discount.TierNumber, totals.Discount.Rate, totals.Discount.Amount, totals.Discount.CapApplied));
            Assert.Equal((104.00m, 98.80m, 14.82m, 113.62m), (totals.Subtotal, totals.Taxable, totals.Vat, totals.Total));
        });
    }

    [Fact]
    public async Task AnActivatedTier_IsCappedAtTheMaximumDiscountPerOrder()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        await manager.ChangeSettingAsync(SettingKeys.MaxDiscountAmount, "50.00");
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        (HttpClient patronClient, Guid patron) = await RegisterPatronAsync();
        using HttpClient client = patronClient;
        await AddOrderAsync(menu, patron, drinks: 2, OrderOutcome.Paid);
        await AddOrderAsync(menu, patron, drinks: 2, OrderOutcome.Paid);

        await WithOnlyTierOneActiveAsync(manager, minPreviousOrders: 2, minDrinksPerOrder: 2, rate: 0.75m, async () =>
        {
            OrderTotalsResponse totals = await QuoteAsync(client, menu, drinks: 4);

            // 75% of 104.00 = 78.00, capped at 50.00; taxable 54.00; VAT 8.10; total 62.10.
            Assert.Equal((50.00m, true), (totals.Discount.Amount, totals.Discount.CapApplied));
            Assert.Equal((54.00m, 8.10m, 62.10m), (totals.Taxable, totals.Vat, totals.Total));
        });
    }

    [Fact]
    public async Task CancelledAndUnpaidOrders_DoNotEarnADiscount()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        (HttpClient patronClient, Guid patron) = await RegisterPatronAsync();
        using HttpClient client = patronClient;
        await AddOrderAsync(menu, patron, drinks: 3, OrderOutcome.Paid);
        await AddOrderAsync(menu, patron, drinks: 3, OrderOutcome.PaidThenCancelled);
        await AddOrderAsync(menu, patron, drinks: 3, OrderOutcome.PaymentFailed);

        await WithOnlyTierOneActiveAsync(manager, minPreviousOrders: 2, minDrinksPerOrder: 2, rate: 0.05m, async () =>
        {
            OrderTotalsResponse totals = await QuoteAsync(client, menu, drinks: 4);

            Assert.Equal((0.00m, (int?)null), (totals.Discount.Amount, totals.Discount.TierNumber));
            Assert.Equal(119.60m, totals.Total);   // 104.00 + VAT 15.60
        });
    }

    [Fact]
    public async Task ADeactivatedTier_NoLongerGivesADiscount()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        (HttpClient patronClient, Guid patron) = await RegisterPatronAsync();
        using HttpClient client = patronClient;
        await AddOrderAsync(menu, patron, drinks: 2, OrderOutcome.Paid);
        await AddOrderAsync(menu, patron, drinks: 2, OrderOutcome.Paid);

        await WithOnlyTierOneActiveAsync(manager, minPreviousOrders: 2, minDrinksPerOrder: 2, rate: 0.05m, async () =>
        {
            DiscountTierResponse active = (await manager.Client.GetFromJsonAsync<List<DiscountTierResponse>>(new Uri(TiersPath, UriKind.Relative)))!
                .Single(t => t.TierNumber == 1);

            using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
                HttpMethod.Post, $"{TiersPath}/1/deactivate", new { version = active.Version });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.ReadJsonAsync<DiscountTierResponse>()).IsActive);
            Assert.Equal(0.00m, (await QuoteAsync(client, menu, drinks: 4)).Discount.Amount);
        });
    }

    private enum OrderOutcome
    {
        Unpaid,
        Paid,
        PaidThenCancelled,
        PaymentFailed,
    }

    private static async Task<OrderTotalsResponse> QuoteAsync(HttpClient patron, TestMenu menu, int drinks)
    {
        object drink = new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id };
        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, QuotePath, new { drinks = Enumerable.Repeat(drink, drinks).ToArray() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync<QuoteResponse>()).Totals
            ?? throw new InvalidOperationException("Expected totals once every drink is complete.");
    }

    /// <summary>Registers a patron through the API (signed in) and returns the client and the patron's id.</summary>
    private async Task<(HttpClient Client, Guid PatronId)> RegisterPatronAsync()
    {
        RegisterBody body = RegisterBody.Valid();
        HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Guid id = await Db(scope).Users.Where(u => u.Email == body.Email).Select(u => u.Id).SingleAsync();
        return (client, id);
    }

    /// <summary>Stores an order for the patron with the given number of drinks and moves it to the given outcome.</summary>
    private async Task AddOrderAsync(TestMenu menu, Guid patron, int drinks, OrderOutcome outcome)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);

        OrderingOptions options = (await scope.ServiceProvider.GetRequiredService<IOrderingCatalogService>().GetOptionsAsync(CancellationToken.None)).Options
            ?? throw new InvalidOperationException("Ordering is unavailable in the test database.");
        string pickupLocal = options.Pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture);
        DraftPricingResult priced = await scope.ServiceProvider.GetRequiredService<IOrderPricingService>().PriceDraftAsync(
            new OrderDraft(menu.Restaurant.Id, pickupLocal, Enumerable.Repeat<DraftDrinkSelection?>(menu.Drink(), drinks).ToList()),
            CancellationToken.None);
        PriceBreakdown price = priced.Price ?? throw new InvalidOperationException("Pricing failed.");

        Order order = Order.Place(patron, OrderReference.New(), new OrderRestaurant(menu.Restaurant.Id, menu.Restaurant.Name, null),
            priced.Draft!.Pickup.Utc.ToUniversalTime(), "UTC", price with { PricedAtUtc = price.PricedAtUtc.ToUniversalTime() },
            Guid.NewGuid(), Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()));

        switch (outcome)
        {
            case OrderOutcome.Paid:
                order.MarkConfirmed();
                break;
            case OrderOutcome.PaidThenCancelled:
                order.MarkConfirmed();
                order.Cancel(order.PickupAtUtc.AddHours(-1));
                break;
            case OrderOutcome.PaymentFailed:
                order.MarkPaymentFailed();
                break;
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Sets tier 1's thresholds and rate and activates it through the manager API (tiers 2 and 3 inactive), runs the
    /// check, then puts all three tiers back exactly as they were.
    /// </summary>
    private async Task WithOnlyTierOneActiveAsync(ManagerSession manager, int minPreviousOrders, int minDrinksPerOrder, decimal rate, Func<Task> check)
    {
        List<(int Number, int Previous, int Drinks, decimal Rate, bool Active)> original;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            List<DiscountTier> tiers = await db.DiscountTiers.ToListAsync();
            original = [.. tiers.Select(t => (t.TierNumber, t.MinPreviousOrders, t.MinDrinksPerOrder, t.DiscountRate, t.IsActive))];
            foreach (DiscountTier tier in tiers.Where(t => t.TierNumber != 1))
            {
                tier.Deactivate();
            }

            await db.SaveChangesAsync();
        }

        try
        {
            DiscountTierResponse tierOne = (await manager.Client.GetFromJsonAsync<List<DiscountTierResponse>>(new Uri(TiersPath, UriKind.Relative)))!
                .Single(t => t.TierNumber == 1);
            using HttpResponseMessage update = await manager.Client.SendWithCsrfAsync(HttpMethod.Put, $"{TiersPath}/1", new
            {
                minPreviousOrders, minDrinksPerOrder, discountRate = rate, version = tierOne.Version,
            });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            DiscountTierResponse updated = await update.ReadJsonAsync<DiscountTierResponse>();

            if (!updated.IsActive)
            {
                Assert.True(updated.CanActivate);
                using HttpResponseMessage activate = await manager.Client.SendWithCsrfAsync(
                    HttpMethod.Post, $"{TiersPath}/1/activate", new { version = updated.Version });
                Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
                Assert.True((await activate.ReadJsonAsync<DiscountTierResponse>()).IsActive);
            }

            await check();
        }
        finally
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            MilkshakeDbContext db = Db(scope);
            foreach (DiscountTier tier in await db.DiscountTiers.ToListAsync())
            {
                (int _, int previous, int drinks, decimal tierRate, bool active) = original.Single(o => o.Number == tier.TierNumber);
                tier.Update(previous, drinks, tierRate);
                if (active)
                {
                    tier.Activate();
                }
                else
                {
                    tier.Deactivate();
                }
            }

            await db.SaveChangesAsync();
        }
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
