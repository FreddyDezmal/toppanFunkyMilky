import { describeError } from '../../lib/api/describeError'
import { ApiError } from '../../lib/api/httpClient'

/** Today's date (yyyy-MM-dd) in the business time zone, so "today" is the shop's day, not the browser's. */
export function todayIn(timeZone?: string): string {
  const format = (zone?: string) =>
    new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  try {
    return format(timeZone)
  } catch {
    return format()
  }
}

/** 2026-11-20 → 2026/11/20, as on the URS example screen. */
export function slashed(date: string): string {
  return date.replaceAll('-', '/')
}

/** Page numbers to offer: the first, the last, and two either side of the current one, with gaps as null. */
export function pageNumbers(current: number, total: number): (number | null)[] {
  const wanted = [...new Set([1, total, current - 2, current - 1, current, current + 1, current + 2])]
    .filter((n) => n >= 1 && n <= total)
    .sort((a, b) => a - b)
  const result: (number | null)[] = []
  let previous = 0
  for (const n of wanted) {
    if (previous > 0 && n - previous > 1) {
      result.push(null)
    }
    result.push(n)
    previous = n
  }
  return result
}

/** Mon, Tue, … in the display locale; index 0 is Monday. */
export function shortWeekday(index: number, locale?: string): string {
  // 1 January 2024 was a Monday.
  const date = new Date(Date.UTC(2024, 0, 1 + index))
  try {
    return new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' }).format(date)
  } catch {
    return ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'][index] ?? ''
  }
}

/** "2026-11" → "Nov 2026" in the display locale. */
export function shortMonth(label: string, locale?: string): string {
  const [year, month] = label.split('-').map(Number)
  if (!year || !month) {
    return label
  }
  try {
    return new Intl.DateTimeFormat(locale, { month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)))
  } catch {
    return label
  }
}

/** The smallest 1, 2 or 5 × 10ⁿ at or above the largest value, so a chart axis has round ticks. */
export function niceMax(value: number): number {
  if (value <= 0) {
    return 1
  }
  const power = 10 ** Math.floor(Math.log10(value))
  return [1, 2, 5, 10].map((step) => step * power).find((step) => step >= value) ?? 10 * power
}

/** A refused filter's own messages (e.g. "Choose a period of at most 366 days."); otherwise the usual description. */
export function describeReportError(error: unknown): string {
  if (error instanceof ApiError && error.status === 400) {
    const messages = Object.values(error.fieldErrors).flat()
    if (messages.length > 0) {
      return messages.join(' ')
    }
  }
  return describeError(error)
}

/** How far the time zone is ahead of UTC at this instant, in milliseconds. */
function zoneOffset(instant: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instant))
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value ?? 0)
  const wall = Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'), part('second'))
  return wall - Math.floor(instant / 1000) * 1000
}

/** The day after a yyyy-MM-dd date. */
export function nextDay(date: string): string {
  const [year, month, day] = date.split('-').map(Number)
  return new Date(Date.UTC(year ?? 2000, (month ?? 1) - 1, (day ?? 1) + 1)).toISOString().slice(0, 10)
}

/** Local midnight of a yyyy-MM-dd date in the business time zone, as a UTC ISO instant (the audit API's format). */
export function startOfDayUtc(date: string, timeZone?: string): string {
  const [year, month, day] = date.split('-').map(Number)
  const wall = Date.UTC(year ?? 2000, (month ?? 1) - 1, day ?? 1)
  try {
    if (!timeZone) {
      throw new RangeError('No time zone.')
    }
    // Two passes: the offset at the first guess may differ from the offset at local midnight (clock changes).
    const first = wall - zoneOffset(wall, timeZone)
    return new Date(wall - zoneOffset(first, timeZone)).toISOString()
  } catch {
    return new Date((year ?? 2000), (month ?? 1) - 1, day ?? 1).toISOString()
  }
}
