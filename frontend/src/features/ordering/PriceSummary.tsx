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
    <dl className="grid grid-cols-[1fr_auto] gap-y-1.5 text-sm tabular-nums">
      <dt className="text-warm-grey">Subtotal</dt>
      <dd className="pl-4 text-right">{money(totals.subtotal)}</dd>

      <dt className="text-warm-grey">Frequent-customer discount</dt>
      <dd className="pl-4 text-right">{discount.tierNumber === null ? `${money(discount.amount)} (none)` : `−${money(discount.amount)}`}</dd>

      <dt className="text-warm-grey">VAT ({formatPercent(totals.vatRate, locale)})</dt>
      <dd className="pl-4 text-right">{money(totals.vat)}</dd>

      <dt className="border-t border-line pt-2 text-base font-semibold text-chocolate">{totalLabel}</dt>
      <dd className="border-t pl-4 border-line pt-2 text-right text-base font-semibold text-chocolate">{money(totals.total)}</dd>
    </dl>
  )
}

/**
 * The same four rows as {@link PriceSummary} with "—" for the amounts, shown until the first estimate arrives so the
 * section keeps its height and nothing below it moves (M14 DEF-04). Hidden from assistive technology: the section's
 * status line already says "Updating prices…" or "Complete every milkshake to see the total."
 */
export function PriceSummaryPlaceholder({ totalLabel = 'Estimated total' }: { totalLabel?: string }) {
  return (
    <dl aria-hidden="true" data-testid="price-summary-placeholder" className="grid grid-cols-[1fr_auto] gap-y-1.5 text-sm text-warm-grey">
      <dt className="text-warm-grey">Subtotal</dt>
      <dd className="pl-4 text-right">—</dd>

      <dt className="text-warm-grey">Frequent-customer discount</dt>
      <dd className="pl-4 text-right">—</dd>

      <dt className="text-warm-grey">VAT</dt>
      <dd className="pl-4 text-right">—</dd>

      <dt className="border-t border-line pt-2 text-base font-semibold text-chocolate">{totalLabel}</dt>
      <dd className="border-t pl-4 border-line pt-2 text-right text-base font-semibold">—</dd>
    </dl>
  )
}
