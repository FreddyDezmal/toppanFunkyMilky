import { formatMoney, formatPercent } from '../../lib/format/money'
import type { OrderTotals } from './types'

/**
 * Server-calculated totals (M6): subtotal, frequent-customer discount (0 when none, as the URS requires), VAT with
 * its rate, and the estimated total. Formatting only – every amount comes from the API.
 */
export function PriceSummary({
  totals,
  locale,
  totalLabel = 'Estimated total',
}: {
  totals: OrderTotals
  locale: string | undefined
  /** "Estimated total" for drafts; "Total" for a placed order, whose amount is fixed. */
  totalLabel?: string
}) {
  const money = (amount: number) => formatMoney(amount, totals.currencyCode, locale)
  const { discount } = totals

  return (
    <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 text-sm">
      <dt className="text-slate-600">Subtotal</dt>
      <dd className="text-right">{money(totals.subtotal)}</dd>

      <dt className="text-slate-600">Frequent-customer discount</dt>
      <dd className="text-right">{discount.tierNumber === null ? `${money(discount.amount)} (none)` : `−${money(discount.amount)}`}</dd>

      <dt className="text-slate-600">VAT ({formatPercent(totals.vatRate, locale)})</dt>
      <dd className="text-right">{money(totals.vat)}</dd>

      <dt className="border-t border-slate-200 pt-1 font-semibold text-slate-900">{totalLabel}</dt>
      <dd className="border-t border-slate-200 pt-1 text-right font-semibold text-slate-900">{money(totals.total)}</dd>
    </dl>
  )
}
