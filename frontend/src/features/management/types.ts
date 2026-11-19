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