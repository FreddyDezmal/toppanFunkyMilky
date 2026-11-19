/**
 * Formats an amount with the configured display locale and the amount's ISO 4217 currency.
 * No currency symbol is hard-coded: Intl derives it from the locale and currency (e.g. en-ZA + ZAR → "R 25,50").
 */
export function formatMoney(amount: number, currencyCode: string, locale?: string): string {
  try {
    return new Intl.NumberFormat(locale, { style: 'currency', currency: currencyCode }).format(amount)
  } catch {
    return `${amount.toFixed(2)} ${currencyCode}`
  }
}

/** Formats a fraction (0.15) as a percentage ("15%") for the configured locale. */
export function formatPercent(fraction: number, locale?: string): string {
  try {
    return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 2 }).format(fraction)
  } catch {
    return `${fraction * 100}%`
  }
}

/** Formats a UTC timestamp from the API in the user's time zone. */
export function formatDateTime(isoUtc: string, locale?: string): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(isoUtc))
  } catch {
    return isoUtc
  }
}