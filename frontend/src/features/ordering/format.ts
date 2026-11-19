import { formatMoney } from '../../lib/format/money'
import type { OrderingOption } from './types'

const LOCAL_TIME = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/

/**
 * Formats a business-local time ("2026-11-20T14:30") for display without converting it through the browser's
 * time zone: the wall-clock fields are formatted as given.
 */
export function formatLocalTime(local: string, locale?: string): string {
  const match = LOCAL_TIME.exec(local)
  if (!match) {
    return local
  }
  const [, year, month, day, hour, minute] = match.map(Number) as [number, number, number, number, number, number]
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'full', timeStyle: 'short', timeZone: 'UTC' }).format(
      new Date(Date.UTC(year, month - 1, day, hour, minute)),
    )
  } catch {
    return local.replace('T', ' ')
  }
}

/** A readable name for an IANA time zone, with the id itself, e.g. "South Africa Standard Time (Africa/Johannesburg)". */
export function timeZoneLabel(timeZone: string, locale?: string): string {
  try {
    const name = new Intl.DateTimeFormat(locale, { timeZone, timeZoneName: 'long' })
      .formatToParts(new Date())
      .find((part) => part.type === 'timeZoneName')?.value
    return name && name !== timeZone ? `${name} (${timeZone})` : timeZone
  } catch {
    return timeZone
  }
}

/** "Vanilla (R 30,00)": the option with its current fee, formatted for the configured locale. */
export function optionLabel(option: OrderingOption, locale?: string): string {
  return `${option.name} (${formatMoney(option.fee.amount, option.fee.currencyCode, locale)})`
}

/** Shown wherever prices appear (M6): every amount is a server estimate, recalculated when the order is placed. */
export const ESTIMATE_NOTE =
  'Prices are estimates based on current fees and VAT. The final amount is calculated when you place your order.'