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
  displayLocale: 'Pricing.DisplayLocale',
  currencyCode: 'Pricing.CurrencyCode',
} as const