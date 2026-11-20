using System.Collections.Concurrent;
using Milkshake.Application.Payments;
using Milkshake.Domain.Payments;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>
/// A simulated Paystack for API tests (decision P10): no network, no real money. Each test sets how checkout creation
/// and verification behave. By default checkouts are created on checkout.paystack.com and verification says "open".
/// </summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    public string Name => Payment.PaystackProvider;

    public string CheckoutHost => "checkout.paystack.com";

    public ConcurrentQueue<CheckoutRequest> Created { get; } = new();

    public Func<CheckoutRequest, Uri> OnCreate { get; set; } = request => new Uri($"https://checkout.paystack.com/{request.Reference.ToLowerInvariant()}");

    public Func<string, GatewayVerification> OnVerify { get; set; } = _ => new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);

    public async Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Created.Enqueue(request);
        return OnCreate(request);
    }

    private int _verifyCalls;

    /// <summary>How many times the provider was asked to verify a transaction.</summary>
    public int VerifyCalls => Volatile.Read(ref _verifyCalls);

    public async Task<GatewayVerification> VerifyAsync(string reference, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _verifyCalls);
        await Task.Yield();
        return OnVerify(reference);
    }

    private static long _nextTransactionId = Random.Shared.NextInt64(1_000_000, 1_000_000_000);

    /// <summary>
    /// A verified payment of the given amount in cents, with its own Paystack transaction id: a transaction belongs to one
    /// payment only (unique index), so tests sharing a database must never reuse an id.
    /// </summary>
    public static GatewayVerification Paid(long cents, string currency = "ZAR") =>
        new(true, VerifiedOutcome.Paid, cents, currency, Interlocked.Increment(ref _nextTransactionId), "Approved");
}
