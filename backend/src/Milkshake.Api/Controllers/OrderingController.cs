using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Api.Security;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Pricing;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Patron ordering: read-only options (M5), draft validation with an estimated price, live quotes (M6) and order
/// placement (M7). Nothing here takes payment or marks an order paid. Rate-limited per patron (finding F1).
/// Prices, VAT rates and discounts always come from the server; requests carrying them are rejected.
/// </summary>
[ApiController]
[Route("api/ordering")]
[Authorize(Policy = AuthorizationPolicies.PatronOnly)]
[EnableRateLimiting(RateLimitPolicies.Ordering)]
public sealed partial class OrderingController(
    IOrderingCatalogService catalog,
    IOrderPricingService pricing,
    IOrderPlacementService orders,
    ILogger<OrderingController> logger) : ControllerBase
{
    /// <summary>
    /// Largest accepted draft or quote body (finding F2). The largest real draft, 50 milkshakes with the longest ids,
    /// is about 4 KB.
    /// </summary>
    public const int MaxDraftBodyBytes = 16 * 1024;

    /// <summary>
    /// Active flavours, consistencies and toppings with current fees, active restaurants, drink limits and the
    /// provisional pickup window. Fees are per choice; drink prices, VAT and totals come from quotes.
    /// </summary>
    /// <response code="200">The options.</response>
    /// <response code="401">Not signed in.</response>
    /// <response code="403">Signed in without the Patron role.</response>
    /// <response code="429">Too many ordering requests; try again in a minute.</response>
    /// <response code="503">Ordering configuration is invalid; details are in the server log.</response>
    [HttpGet("options")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<OrderingOptionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderingOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        OrderingOptionsResult result = await catalog.GetOptionsAsync(cancellationToken);

        return result.Options is null
            ? Unavailable()
            : Ok(OrderingOptionsResponse.From(result.Options));
    }

    /// <summary>
    /// Live, non-binding price estimate for the milkshakes being chosen: a price per complete milkshake and, once all
    /// are complete, the subtotal, discount, VAT and total. Incomplete milkshakes are allowed. Stores nothing.
    /// </summary>
    /// <response code="200">The estimate (<c>totals</c> is null until every milkshake is complete).</response>
    /// <response code="400">Unknown or unavailable choices, a quantity outside the limits, or a malformed request.</response>
    /// <response code="413">The request body is larger than any real draft.</response>
    /// <response code="429">Too many ordering requests; try again in a minute.</response>
    /// <response code="503">Ordering or pricing configuration is invalid.</response>
    [HttpPost("quote")]
    [RequestBodyLimit(MaxDraftBodyBytes)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<QuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QuoteResponse>> Quote(QuoteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DrinksQuoteResult result;
        try
        {
            result = await pricing.QuoteDrinksAsync(request.ToSelections(), cancellationToken);
        }
        catch (PricingConfigurationException exception)
        {
            LogPricingConfiguration(logger, exception);
            return Unavailable();
        }

        if (result.OrderingUnavailable)
        {
            return Unavailable();
        }

        return result.Quote is null
            ? FieldErrors(result.Errors)
            : Ok(QuoteResponse.From(result.Quote));
    }

    /// <summary>
    /// Checks a complete draft against the options in force now – drink count within the configured limits, exactly
    /// one active flavour, consistency and topping per drink, an active restaurant, and a pickup time inside the
    /// provisional pickup window – and returns it with an estimated price. Stores nothing.
    /// </summary>
    /// <response code="200">Every selection is valid; the resolved draft summary with estimated totals.</response>
    /// <response code="400">Missing, unavailable or malformed selections, keyed by field (e.g. drinks[1].toppingId).</response>
    /// <response code="413">The request body is larger than any real draft.</response>
    /// <response code="429">Too many ordering requests; try again in a minute.</response>
    /// <response code="503">Ordering or pricing configuration is invalid.</response>
    [HttpPost("draft/validate")]
    [RequestBodyLimit(MaxDraftBodyBytes)]
    [ProducesResponseType<DraftSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DraftSummaryResponse>> ValidateDraft(ValidateDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DraftPricingResult result;
        try
        {
            result = await pricing.PriceDraftAsync(request.ToDraft(), cancellationToken);
        }
        catch (PricingConfigurationException exception)
        {
            LogPricingConfiguration(logger, exception);
            return Unavailable();
        }

        if (result.OrderingUnavailable)
        {
            return Unavailable();
        }

        return result.Draft is null || result.Price is null
            ? FieldErrors(result.Errors)
            : Ok(DraftSummaryResponse.From(result.Draft, result.Price));
    }

    /// <summary>
    /// Places an order: the draft is revalidated and repriced on the server and stored with its price snapshot, unpaid
    /// (status PendingPayment). The Idempotency-Key header (a UUID) makes retries safe: the same key with the same
    /// content returns the original order instead of creating another one.
    /// </summary>
    /// <response code="201">The order was placed. Location points to it.</response>
    /// <response code="200">A retry of an order already placed with this key: the original order, unchanged.</response>
    /// <response code="400">Invalid selections, pickup time, request shape or Idempotency-Key header.</response>
    /// <response code="409">The price changed since the patron reviewed it; <c>currentTotals</c> holds the new price. Nothing was stored.</response>
    /// <response code="413">The request body is larger than any real order.</response>
    /// <response code="422">The key was already used for a different order. Nothing was stored.</response>
    /// <response code="429">Too many ordering requests; try again in a minute.</response>
    /// <response code="503">Ordering or pricing configuration is invalid.</response>
    [HttpPost("orders")]
    [RequestBodyLimit(MaxDraftBodyBytes)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderResponse>> PlaceOrder(
        PlaceOrderRequest request,
        [FromHeader(Name = OrderMessages.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Guid.TryParseExact(idempotencyKey, "D", out Guid key) || key == Guid.Empty)
        {
            ModelState.AddModelError(OrderMessages.IdempotencyKeyHeader, OrderMessages.IdempotencyKeyRequired);
        }

        if (request.ExpectedTotal is null || string.IsNullOrWhiteSpace(request.ExpectedCurrencyCode))
        {
            ModelState.AddModelError("expectedTotal", OrderMessages.ExpectedTotalRequired);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        PlaceOrderResult result;
        try
        {
            result = await orders.PlaceAsync(
                new PlaceOrderCommand(request.ToDraft(), request.ExpectedTotal!.Value, request.ExpectedCurrencyCode!.Trim(), key),
                cancellationToken);
        }
        catch (PricingConfigurationException exception)
        {
            LogPricingConfiguration(logger, exception);
            return Unavailable();
        }

        switch (result.Outcome)
        {
            case PlaceOrderOutcome.Created:
                OrderResponse created = OrderResponse.From(result.Order!);
                return Created($"/api/ordering/orders/{created.Reference}", created);
            case PlaceOrderOutcome.Replayed:
                return Ok(OrderResponse.From(result.Order!));
            case PlaceOrderOutcome.Invalid:
                return FieldErrors(result.Errors);
            case PlaceOrderOutcome.PriceChanged:
                ProblemDetails changed = ProblemDetailsFactory.CreateProblemDetails(
                    HttpContext, StatusCodes.Status409Conflict, OrderMessages.PriceChangedTitle, detail: OrderMessages.PriceChangedDetail);
                changed.Extensions["currentTotals"] = OrderTotalsResponse.From(result.CurrentPrice!);
                return Conflict(changed);
            case PlaceOrderOutcome.KeyReused:
                return Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: OrderMessages.KeyReusedTitle,
                    detail: OrderMessages.KeyReusedDetail);
            default:
                return Unavailable();
        }
    }

    private ActionResult FieldErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach ((string field, string[] messages) in errors)
        {
            foreach (string message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }

        return ValidationProblem(ModelState);
    }

    private ObjectResult Unavailable() =>
        Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: OrderingMessages.UnavailableTitle,
            detail: OrderingMessages.UnavailableDetail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ordering is unavailable because prices or settings cannot produce a correct price.")]
    private static partial void LogPricingConfiguration(ILogger logger, Exception exception);
}