import { getFile, getJson, postJson, putJson, type DownloadedFile } from '../../lib/api/httpClient'
import type {
  AuditChange,
  AuditPage,
  CatalogItem,
  CatalogKind,
  DiscountTier,
  EmailDelivery,
  EmailDeliveryList,
  EmailStatus,
  ManagerOrder,
  OrderReport,
  OrderTrends,
  PriceHistory,
  RefundNeedingAttention,
  ReportStatusFilter,
  Restaurant,
  SecurityEvent,
  SecurityEventType,
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

export function setDiscountTierActive(tierNumber: number, active: boolean, version: number): Promise<DiscountTier> {
  return postJson<DiscountTier>(`${MANAGER}/discount-tiers/${tierNumber}/${active ? 'activate' : 'deactivate'}`, { version })
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

/** The newest emails (up to 100), optionally of one status and/or one order, with counts per status (M9). */
export function listEmails(filter: { status?: EmailStatus; order?: string }, signal?: AbortSignal): Promise<EmailDeliveryList> {
  const query = new URLSearchParams()
  if (filter.status) {
    query.set('status', filter.status)
  }
  if (filter.order?.trim()) {
    query.set('order', filter.order.trim())
  }
  const search = query.toString()
  const suffix = search ? `?${search}` : ''
  return getJson<EmailDeliveryList>(`${MANAGER}/emails${suffix}`, { signal })
}

/** Queues a sent or failed email to be sent again (same content and recipient); the server sends it shortly. */
export function resendEmail(id: number): Promise<EmailDelivery> {
  return postJson<EmailDelivery>(`${MANAGER}/emails/${id}/resend`)
}

/** Filters of the orders report: a business-local date, or an inclusive range (at most 366 days). */
export interface OrderReportFilter {
  from: string
  to?: string
  status?: ReportStatusFilter
  q?: string
  page?: number
}

function orderReportQuery(filter: OrderReportFilter): URLSearchParams {
  const query = new URLSearchParams({ from: filter.from })
  if (filter.to && filter.to !== filter.from) {
    query.set('to', filter.to)
  }
  if (filter.status && filter.status !== 'all') {
    query.set('status', filter.status)
  }
  if (filter.q?.trim()) {
    query.set('q', filter.q.trim())
  }
  if (filter.page && filter.page > 1) {
    query.set('page', String(filter.page))
  }
  return query
}

/** One page of the orders report: one row per milkshake, newest order first (AC-12.2). */
export function getOrderReport(filter: OrderReportFilter, signal?: AbortSignal): Promise<OrderReport> {
  return getJson<OrderReport>(`${MANAGER}/reports/orders?${orderReportQuery(filter).toString()}`, { signal })
}

/** Every row of the orders report with these filters, as CSV (at most 10,000; AC-12.6). */
export function exportOrderReport(filter: OrderReportFilter): Promise<DownloadedFile> {
  return getFile(`${MANAGER}/reports/orders/export?${orderReportQuery({ ...filter, page: undefined }).toString()}`)
}

function trendsQuery(filter: { from?: string; to?: string }): string {
  const query = new URLSearchParams()
  if (filter.from) {
    query.set('from', filter.from)
    if (filter.to && filter.to !== filter.from) {
      query.set('to', filter.to)
    }
  }
  const search = query.toString()
  return search ? `?${search}` : ''
}

/** Order volumes by weekday, month and year (AC-12.3); without from, all orders. */
export function getOrderTrends(filter: { from?: string; to?: string }, signal?: AbortSignal): Promise<OrderTrends> {
  return getJson<OrderTrends>(`${MANAGER}/reports/trends${trendsQuery(filter)}`, { signal })
}

/** The trends for this period as CSV: one line per weekday, month and year. */
export function exportOrderTrends(filter: { from?: string; to?: string }): Promise<DownloadedFile> {
  return getFile(`${MANAGER}/reports/trends/export${trendsQuery(filter)}`)
}

/** Filters of the audit history (AC-11.4); from and to are UTC instants (ISO 8601), to exclusive. */
export interface AuditFilter {
  from?: string
  to?: string
  user?: string
  actor?: string
  entity?: string
  action?: AuditChange['action']
  q?: string
}

function auditQuery(values: Record<string, string | undefined>, cursor?: string | null): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(values)) {
    if (value?.trim()) {
      query.set(key, value.trim())
    }
  }
  if (cursor) {
    query.set('cursor', cursor)
  }
  const search = query.toString()
  return search ? `?${search}` : ''
}

/** One page of field-level change history, newest first (AC-11.4, AC-12.4, AC-12.5). */
export function listAuditChanges(filter: AuditFilter, cursor?: string | null, signal?: AbortSignal): Promise<AuditPage<AuditChange>> {
  return getJson<AuditPage<AuditChange>>(`${MANAGER}/audit/changes${auditQuery({ ...filter }, cursor)}`, { signal })
}

/** One page of sign-in, sign-out and lockout events, newest first (T11.3). */
export function listSecurityEvents(
  filter: { from?: string; to?: string; user?: string; eventType?: SecurityEventType },
  cursor?: string | null,
  signal?: AbortSignal,
): Promise<AuditPage<SecurityEvent>> {
  return getJson<AuditPage<SecurityEvent>>(`${MANAGER}/audit/security${auditQuery({ ...filter }, cursor)}`, { signal })
}

/** The change history with these filters as CSV, one line per changed field (at most 10,000 records; masked). */
export function exportAuditChanges(filter: AuditFilter): Promise<DownloadedFile> {
  return getFile(`${MANAGER}/reports/audit/changes/export${auditQuery({ ...filter })}`)
}

/** Sign-in events with these filters as CSV (at most 10,000; masked). */
export function exportSecurityEvents(filter: { from?: string; to?: string; user?: string; eventType?: SecurityEventType }): Promise<DownloadedFile> {
  return getFile(`${MANAGER}/reports/audit/security/export${auditQuery({ ...filter })}`)
}
