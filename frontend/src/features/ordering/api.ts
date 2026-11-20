import { getJson, postJson } from '../../lib/api/httpClient'
import type {
  DraftSummary,
  DrinkSelection,
  OrderCancellation,
  OrderDraft,
  OrderHistoryPage,
  OrderingOptions,
  PaymentCheckout,
  PaymentStatus,
  PendingCancellationRequest,
  PlacedOrder,
  Quote,
} from './types'

const ORDERING = '/api/ordering'

/** Active options with current fees, active restaurants, drink limits and the provisional pickup window. */
export function fetchOrderingOptions(signal?: AbortSignal): Promise<OrderingOptions> {
  return getJson<OrderingOptions>(`${ORDERING}/options`, { signal })
}

/**
 * The request body for draft validation. Fields are copied one by one, so nothing else that might be on
 * the object (a name, a fee) is ever sent: the API rejects unknown fields, and prices never come from the browser.
 */
export function toValidateRequest(draft: OrderDraft) {
  return {
    restaurantId: draft.restaurantId,
    pickupLocal: draft.pickupLocal,
    drinks: draft.drinks.map(toDrinkRequest),
  }
}

/** One milkshake's identifiers, copied field by field. */
function toDrinkRequest(drink: DrinkSelection) {
  return { flavourId: drink.flavourId, consistencyId: drink.consistencyId, toppingId: drink.toppingId }
}

/** Checks the draft against the options in force now. Stores nothing on the server. */
export function validateDraft(draft: OrderDraft, signal?: AbortSignal): Promise<DraftSummary> {
  return postJson<DraftSummary>(`${ORDERING}/draft/validate`, toValidateRequest(draft), { signal })
}

/**
 * A live, non-binding price estimate for the milkshakes being chosen. Only identifiers are sent; the server decides
 * every amount. Incomplete milkshakes are allowed (they come back without a price).
 */
export function quoteDrinks(drinks: DrinkSelection[], signal?: AbortSignal): Promise<Quote> {
  return postJson<Quote>(`${ORDERING}/quote`, { drinks: drinks.map(toDrinkRequest) }, { signal })
}

export const IDEMPOTENCY_HEADER = 'Idempotency-Key'

/**
 * Places the order (M7). The server revalidates and reprices it; `expected` is the total the patron confirmed and is
 * only compared with the server's price (a 409 means it changed). The key identifies this submission: sending the
 * same draft with the same key again returns the original order instead of creating another one.
 */
export function placeOrder(
  draft: OrderDraft,
  expected: { total: number; currencyCode: string },
  idempotencyKey: string,
  signal?: AbortSignal,
): Promise<PlacedOrder> {
  return postJson<PlacedOrder>(
    `${ORDERING}/orders`,
    { ...toValidateRequest(draft), expectedTotal: expected.total, expectedCurrencyCode: expected.currencyCode },
    { signal, headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } },
  )
}

/** One of the signed-in patron's own orders, as stored. */
export function fetchOrder(reference: string, signal?: AbortSignal): Promise<PlacedOrder> {
  return getJson<PlacedOrder>(`${ORDERING}/orders/${encodeURIComponent(reference)}`, { signal })
}

/**
 * Starts (or resumes) paying for the order (M8). No amount is sent: the server charges the stored order total. The key
 * identifies this click: sending it again returns the same checkout instead of creating another payment attempt.
 */
export function startPayment(reference: string, idempotencyKey: string, signal?: AbortSignal): Promise<PaymentCheckout> {
  return postJson<PaymentCheckout>(`${ORDERING}/orders/${encodeURIComponent(reference)}/payments`, {}, {
    signal,
    headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
  })
}

/** Asks the server to check the order's open payment with the provider, then returns what it stored. */
export function refreshPayment(reference: string, signal?: AbortSignal): Promise<PaymentStatus> {
  return postJson<PaymentStatus>(`${ORDERING}/orders/${encodeURIComponent(reference)}/payments/refresh`, {}, { signal })
}

/** The order's cancellation quote, a manager's open request and its refunds (M8b). */
export function fetchCancellation(reference: string, signal?: AbortSignal): Promise<OrderCancellation> {
  return getJson<OrderCancellation>(`${ORDERING}/orders/${encodeURIComponent(reference)}/cancellation`, { signal })
}

/**
 * Cancels the order. `expectedRefund` is the refund the patron confirmed (0 if unpaid); the server only compares it and
 * answers 409 with the new figures in `cancellation` if they changed.
 */
export function cancelOrder(reference: string, expectedRefund: number): Promise<OrderCancellation> {
  return postJson<OrderCancellation>(`${ORDERING}/orders/${encodeURIComponent(reference)}/cancel`, { expectedRefund })
}

/** Accepts the manager's request: the order is cancelled and, if paid, refunded in full. */
export function acceptCancellationRequest(reference: string, expectedRefund: number): Promise<OrderCancellation> {
  return postJson<OrderCancellation>(`${ORDERING}/orders/${encodeURIComponent(reference)}/cancellation-request/accept`, { expectedRefund })
}

/** Declines the manager's request; the order is not changed. */
export function declineCancellationRequest(reference: string): Promise<OrderCancellation> {
  return postJson<OrderCancellation>(`${ORDERING}/orders/${encodeURIComponent(reference)}/cancellation-request/decline`, {})
}

/** The patron's orders with a manager request waiting for an answer (Home page notice). */
export function fetchPendingCancellationRequests(signal?: AbortSignal): Promise<PendingCancellationRequest[]> {
  return getJson<PendingCancellationRequest[]>(`${ORDERING}/cancellation-requests`, { signal })
}

/** One page of the signed-in patron's order history, newest first; pass the previous page's nextCursor for more. */
export function fetchOrders(cursor?: string | null, signal?: AbortSignal): Promise<OrderHistoryPage> {
  const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  return getJson<OrderHistoryPage>(`${ORDERING}/orders${query}`, { signal })
}
