import type { PaymentLabel, RefundLabel } from '../ordering/types'

/** The three priced catalogue lists, as they appear in API routes. */
export const CATALOG_KINDS = ['flavours', 'toppings', 'consistencies'] as const
export type CatalogKind = (typeof CATALOG_KINDS)[number]

export const CATALOG_LABELS: Record<CatalogKind, { title: string; singular: string }> = {
  flavours: { title: 'Flavours', singular: 'flavour' },
  toppings: { title: 'Toppings', singular: 'topping' },
  consistencies: { title: 'Consistencies', singular: 'consistency' },
}

export function isCatalogKind(value: string | undefined): value is CatalogKind {
  return (CATALOG_KINDS as readonly string[]).includes(value ?? '')
}

export type StatusFilter = 'all' | 'active' | 'inactive'

/** GET /api/manager/catalog/{kind}. Send `version` back with any change. */
export interface CatalogItem {
  id: number
  name: string
  displayOrder: number
  isActive: boolean
  currentPrice: number | null
  currencyCode: string | null
  scheduledPrice: number | null
  /** UTC ISO 8601. */
  scheduledFrom: string | null
  version: number
}

/** GET /api/manager/settings. Values are invariant-culture text. */
export interface Setting {
  key: string
  value: string
  valueType: string
  description: string | null
  isEditable: boolean
  hint: string
  version: number
}

export const SETTING_KEYS = {
  minDrinksPerOrder: 'Orders.MinDrinksPerOrder',
  maxDrinksPerOrder: 'Orders.MaxDrinksPerOrder',
  vatRate: 'Tax.VatRate',
  maxDiscountAmount: 'Discounts.MaxDiscountAmount',
  refundFeeRate: 'Refund.FeeRate',
  minPickupLeadTimeMinutes: 'Pickup.MinLeadTimeMinutes',
  pickupMaxAdvanceDays: 'Pickup.MaxAdvanceDays',
  pickupTimeZone: 'Pickup.TimeZone',
  displayLocale: 'Pricing.DisplayLocale',
  currencyCode: 'Pricing.CurrencyCode',
} as const

const SETTING_LABELS: Record<string, string> = {
  [SETTING_KEYS.minDrinksPerOrder]: 'Minimum drinks per order',
  [SETTING_KEYS.maxDrinksPerOrder]: 'Maximum drinks per order',
  [SETTING_KEYS.vatRate]: 'VAT rate',
  [SETTING_KEYS.maxDiscountAmount]: 'Maximum discount amount',
  [SETTING_KEYS.refundFeeRate]: 'Cancellation fee rate',
  [SETTING_KEYS.minPickupLeadTimeMinutes]: 'Minimum pickup lead time',
  [SETTING_KEYS.pickupMaxAdvanceDays]: 'Maximum days ahead for pickup',
  [SETTING_KEYS.pickupTimeZone]: 'Pickup time zone',
  [SETTING_KEYS.displayLocale]: 'Display locale',
  [SETTING_KEYS.currencyCode]: 'Currency',
}

/** A manager-friendly name for a setting key. */
export function settingLabel(key: string): string {
  return SETTING_LABELS[key] ?? key
}

export type PriceStatus = 'Past' | 'Current' | 'Scheduled'

/** One price period. Times are UTC ISO 8601. */
export interface PricePeriod {
  id: number
  amount: number
  currencyCode: string
  effectiveFrom: string
  effectiveTo: string | null
  status: PriceStatus
  canCancel: boolean
}

/** GET /api/manager/catalog/{kind}/{id}/prices: the item (with its current version) and its history, newest first. */
export interface PriceHistory {
  item: CatalogItem
  prices: PricePeriod[]
}

/** GET /api/manager/restaurants. */
export interface Restaurant {
  id: number
  name: string
  address: string | null
  isActive: boolean
  version: number
}

/** GET /api/manager/discount-tiers. Rates are fractions (0.05 = 5%). */
export interface DiscountTier {
  tierNumber: number
  minPreviousOrders: number
  minDrinksPerOrder: number
  discountRate: number
  isActive: boolean
  canActivate: boolean
  activationBlockedReason: string | null
  version: number
}
/** GET /api/manager/orders/{reference}: an order as a manager sees it (M8b, Q3). */
export interface ManagerOrder {
  reference: string
  status: 'PendingPayment' | 'Confirmed' | 'PaymentFailed' | 'Cancelled'
  isPaid: boolean
  patronFirstName: string
  patronEmail: string
  restaurantName: string
  /** UTC ISO 8601. */
  pickupAtUtc: string
  pickupTimeZone: string
  total: number
  currencyCode: string
  placedAt: string
  canRequestCancellation: boolean
  requests: ManagerCancellationRequest[]
  refunds: ManagerRefund[]
}

export interface ManagerCancellationRequest {
  id: number
  reason: string
  status: 'Pending' | 'Accepted' | 'Declined' | 'Lapsed'
  isOpen: boolean
  refundAmount: number
  createdAt: string
  closedAt: string | null
}

export interface ManagerRefund {
  reference: string
  kind: 'PatronCancellation' | 'RestaurantCancellation' | 'UnacceptedPayment'
  status: 'Initiating' | 'Pending' | 'Processed' | 'Failed'
  amount: number
  fee: number
  needsAttention: boolean
  createdAt: string
  completedAt: string | null
}

/** GET /api/manager/refunds/attention: a refund to handle by hand at Paystack (Q4). */
export interface RefundNeedingAttention {
  orderReference: string
  refundReference: string
  kind: ManagerRefund['kind']
  status: ManagerRefund['status']
  amount: number
  currencyCode: string
  providerMessage: string | null
  createdAt: string
}

/** Longest cancellation reason a manager may give (B1.2). */
export const REASON_MAX_LENGTH = 50

/** Delivery state of a transactional email (M9). */
export const EMAIL_STATUSES = ['Pending', 'Sending', 'Sent', 'Failed', 'Suppressed'] as const
export type EmailStatus = (typeof EMAIL_STATUSES)[number]

/** GET /api/manager/emails: one email's delivery state (never its content), AC-9.5. */
export interface EmailDelivery {
  id: number
  kind: 'OrderConfirmed' | 'CancellationRequested' | 'OrderCancelled' | 'RefundProcessed' | 'RefundFailed'
  status: EmailStatus
  orderReference: string
  recipientEmail: string
  attemptCount: number
  lastError: 'Timeout' | 'ProviderUnavailable' | 'RateLimited' | 'Rejected' | 'InvalidRecipient' | null
  createdAt: string
  lastAttemptAt: string | null
  /** Only while waiting to be sent. */
  nextAttemptAt: string | null
  sentAt: string | null
  provider: string | null
  resendCount: number
  canResend: boolean
}

export interface EmailDeliveryList {
  items: EmailDelivery[]
  /** Emails in each status overall. */
  counts: Record<EmailStatus, number>
}

/** Which orders a report shows, by payment status (URS §3.4.4). */
export type ReportStatusFilter = 'all' | 'paid' | 'unpaid' | 'cancelled'

/** One milkshake in the orders report; date (yyyy-MM-dd) and time (HH:mm) are when it was placed, business-local. */
export interface OrderReportRow {
  reference: string
  date: string
  time: string
  placedAtUtc: string
  /** Pickup time in the order's own time zone, "yyyy-MM-ddTHH:mm". */
  pickupLocal: string
  restaurant: string
  drinkNumber: number
  flavour: string
  topping: string
  consistency: string
  paymentStatus: PaymentLabel
  refundStatus: RefundLabel | null
}

/** A page of the orders report with the totals of the whole filtered result (AC-12.2). */
export interface OrderReport {
  from: string
  to: string
  timeZone: string
  totalOrders: number
  totalDrinks: number
  page: number
  pageSize: number
  totalPages: number
  rows: OrderReportRow[]
}

/** One bar: a weekday (Monday…Sunday), a month (yyyy-MM) or a year (yyyy). */
export interface TrendPoint {
  label: string
  orders: number
  paidOrders: number
}

/** Order volumes by weekday, month and year (AC-12.3); from and to are null for all orders. */
export interface OrderTrends {
  from: string | null
  to: string | null
  timeZone: string
  totalOrders: number
  totalPaidOrders: number
  weekdays: TrendPoint[]
  months: TrendPoint[]
  years: TrendPoint[]
}

/** One changed field of an audit record: values as stored (JSON); personal data arrives masked as "***". */
export interface AuditFieldChange {
  field: string
  old: unknown
  new: unknown
}

/** One audit record (AC-11.1): who, what, old, new, action and when. */
export interface AuditChange {
  id: number
  occurredAt: string
  action: 'Insert' | 'Update' | 'Delete'
  entity: string
  recordId: string
  /** user, anonymous or system:…; null for records written before actors were recorded. */
  actor: string | null
  userId: string | null
  user: string | null
  changes: AuditFieldChange[]
  traceId: string | null
}

export type SecurityEventType = 'LoginSucceeded' | 'LoginFailed' | 'LoginLockedOut' | 'Logout' | 'ManagerProvisioned' | 'ReportExported'

/** One sign-in, sign-out or lockout (T11.3); addresses and IPs masked except managers' addresses. */
export interface SecurityEvent {
  id: number
  occurredAt: string
  eventType: SecurityEventType
  userId: string | null
  user: string | null
  ipAddress: string | null
}

/** A page of audit history, newest first; nextCursor is null on the last page. */
export interface AuditPage<T> {
  items: T[]
  nextCursor: string | null
}
