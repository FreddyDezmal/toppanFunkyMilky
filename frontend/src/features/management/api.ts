import { getJson, postJson, putJson } from '../../lib/api/httpClient'
import type {
  CatalogItem,
  CatalogKind,
  DiscountTier,
  ManagerOrder,
  PriceHistory,
  RefundNeedingAttention,
  Restaurant,
  Setting,
  StatusFilter,
} from './types'

const MANAGER = '/api/manager'

export function listCatalogItems(kind: CatalogKind, status: StatusFilter, signal?: AbortSignal): Promise<CatalogItem[]> {
  return getJson<CatalogItem[]>(`${MANAGER}/catalog/${kind}?status=${status}`, { signal })
}

export function createCatalogItem(
  kind: CatalogKind,
  input: { name: string; displayOrder: number; initialPrice: number },
): Promise<CatalogItem> {
  return postJson<CatalogItem>(`${MANAGER}/catalog/${kind}`, input)
}

export function updateCatalogItem(
  kind: CatalogKind,
  id: number,
  input: { name: string; displayOrder: number; version: number },
): Promise<CatalogItem> {
  return putJson<CatalogItem>(`${MANAGER}/catalog/${kind}/${id}`, input)
}

export function setCatalogItemActive(kind: CatalogKind, id: number, active: boolean, version: number): Promise<CatalogItem> {
  return postJson<CatalogItem>(`${MANAGER}/catalog/${kind}/${id}/${active ? 'activate' : 'deactivate'}`, { version })
}

export function getPriceHistory(kind: CatalogKind, id: number, signal?: AbortSignal): Promise<PriceHistory> {
  return getJson<PriceHistory>(`${MANAGER}/catalog/${kind}/${id}/prices`, { signal })
}

/** effectiveFrom: UTC ISO string, or null for "immediately". */
export function schedulePrice(
  kind: CatalogKind,
  id: number,
  input: { amount: number; effectiveFrom: string | null; version: number },
): Promise<PriceHistory> {
  return postJson<PriceHistory>(`${MANAGER}/catalog/${kind}/${id}/prices`, input)
}

export function cancelScheduledPrice(kind: CatalogKind, id: number, priceId: number, version: number): Promise<PriceHistory> {
  return postJson<PriceHistory>(`${MANAGER}/catalog/${kind}/${id}/prices/${priceId}/cancel`, { version })
}

export function listRestaurants(status: StatusFilter, signal?: AbortSignal): Promise<Restaurant[]> {
  return getJson<Restaurant[]>(`${MANAGER}/restaurants?status=${status}`, { signal })
}

export function createRestaurant(input: { name: string; address: string | null }): Promise<Restaurant> {
  return postJson<Restaurant>(`${MANAGER}/restaurants`, input)
}

export function updateRestaurant(
  id: number,
  input: { name: string; address: string | null; version: number },
): Promise<Restaurant> {
  return putJson<Restaurant>(`${MANAGER}/restaurants/${id}`, input)
}

export function setRestaurantActive(id: number, active: boolean, version: number): Promise<Restaurant> {
  return postJson<Restaurant>(`${MANAGER}/restaurants/${id}/${active ? 'activate' : 'deactivate'}`, { version })
}

export function listSettings(signal?: AbortSignal): Promise<Setting[]> {
  return getJson<Setting[]>(`${MANAGER}/settings`, { signal })
}

/** Returns all settings: saving minimum or maximum drinks also changes the other one's version. */
export function updateSetting(key: string, value: string, version: number): Promise<Setting[]> {
  return putJson<Setting[]>(`${MANAGER}/settings/${encodeURIComponent(key)}`, { value, version })
}

export function listDiscountTiers(signal?: AbortSignal): Promise<DiscountTier[]> {
  return getJson<DiscountTier[]>(`${MANAGER}/discount-tiers`, { signal })
}

export function updateDiscountTier(
  tierNumber: number,
  input: { minPreviousOrders: number; minDrinksPerOrder: number; discountRate: number; version: number },
): Promise<DiscountTier> {
  return putJson<DiscountTier>(`${MANAGER}/discount-tiers/${tierNumber}`, input)
}
/** One order by its reference (as typed; the server tolerates case and spaces). */
export function getManagerOrder(reference: string, signal?: AbortSignal): Promise<ManagerOrder> {
  return getJson<ManagerOrder>(`${MANAGER}/orders/${encodeURIComponent(reference.trim())}`, { signal })
}

/** Asks the patron to accept cancelling their order; nothing changes until they accept (B1.2). */
export function requestCancellation(reference: string, reason: string): Promise<ManagerOrder> {
  return postJson<ManagerOrder>(`${MANAGER}/orders/${encodeURIComponent(reference)}/cancellation-requests`, { reason })
}

/** Refunds that failed at Paystack or need the patron's bank details (Q4). */
export function listRefundsNeedingAttention(signal?: AbortSignal): Promise<RefundNeedingAttention[]> {
  return getJson<RefundNeedingAttention[]>(`${MANAGER}/refunds/attention`, { signal })
}
