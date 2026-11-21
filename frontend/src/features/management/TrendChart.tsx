import { useId } from 'react'
import { niceMax } from './reports'
import type { TrendPoint } from './types'

const HEIGHT = 180
const TOP = 12
const BOTTOM = 28
const LEFT = 40
const BAR = 22
const GAP = 14
const PLOT = HEIGHT - TOP - BOTTOM

/** Colours checked with the data-viz palette validator; the lighter one is below 3:1, so every chart has a table view. */
const ALL_FILL = '#f472b6'
const PAID_FILL = '#9d174d'

/** A bar whose top corners are rounded and whose foot sits square on the baseline. */
function barPath(x: number, y: number, width: number, height: number): string {
  if (height <= 0) {
    return ''
  }
  const r = Math.min(4, width / 2, height)
  const bottom = y + height
  return `M${x},${bottom}V${y + r}Q${x},${y} ${x + r},${y}H${x + width - r}Q${x + width},${y} ${x + width},${y + r}V${bottom}Z`
}

interface TrendChartProps {
  /** Visible chart heading, e.g. "Weekly". */
  title: string
  /** What the bars are, e.g. "Orders by day of the week". */
  description: string
  points: TrendPoint[]
  /** Short axis label for a point, e.g. "Mon" or "Nov 2026". */
  label: (point: TrendPoint, index: number) => string
}

/**
 * A two-tone bar chart: all orders (light) with paid orders (dark) in front, as on the URS example screen. Each bar has
 * a hover title, the legend names both tones, and "Show the numbers" opens the same data as a table.
 */
export function TrendChart({ title, description, points, label }: TrendChartProps) {
  const headingId = useId()
  const max = niceMax(Math.max(0, ...points.map((point) => point.orders)))
  const width = LEFT + Math.max(points.length, 1) * (BAR + GAP) + GAP
  const y = (value: number) => TOP + PLOT - (value / max) * PLOT
  const ticks = max >= 2 && max % 2 === 0 ? [0, max / 2, max] : [0, max]
  const totalOrders = points.reduce((sum, point) => sum + point.orders, 0)
  const totalPaid = points.reduce((sum, point) => sum + point.paidOrders, 0)

  return (
    <section aria-labelledby={headingId} className="rounded-xl p-4 ring-1 ring-slate-200">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-600">Orders</p>
      <h3 id={headingId} className="text-lg font-semibold">
        {title}
      </h3>
      <p className="text-sm text-slate-600">{description}</p>

      <ul className="mt-2 flex flex-wrap gap-4 text-xs text-slate-700" aria-label={`${title} legend`}>
        <li className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-3 w-3 rounded-sm" style={{ backgroundColor: ALL_FILL }} />
          All orders ({totalOrders})
        </li>
        <li className="flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-3 w-3 rounded-sm" style={{ backgroundColor: PAID_FILL }} />
          Paid ({totalPaid})
        </li>
      </ul>

      {points.length === 0 ? (
        <p className="mt-3 text-sm text-slate-600">No orders in this period.</p>
      ) : (
        <div className="mt-2 overflow-x-auto">
          <svg
            role="img"
            aria-label={`${title}: ${description}. ${totalOrders} orders, ${totalPaid} paid. The numbers are also in the table below.`}
            viewBox={`0 0 ${width} ${HEIGHT}`}
            width={width}
            height={HEIGHT}
            className="max-w-none text-slate-600"
          >
            {ticks.map((tick) => (
              <g key={tick}>
                <line x1={LEFT} x2={width} y1={y(tick)} y2={y(tick)} stroke="#e2e8f0" strokeWidth={1} />
                <text x={LEFT - 6} y={y(tick)} dy="0.32em" textAnchor="end" fontSize={11} fill="currentColor">
                  {tick}
                </text>
              </g>
            ))}
            {points.map((point, index) => {
              const x = LEFT + GAP + index * (BAR + GAP)
              return (
                <g key={point.label}>
                  <title>{`${label(point, index)}: ${point.orders} ${point.orders === 1 ? 'order' : 'orders'}, ${point.paidOrders} paid`}</title>
                  {/* A transparent hit area taller than the bar, so small bars are easy to hover. */}
                  <rect x={x - GAP / 2} y={TOP} width={BAR + GAP} height={PLOT} fill="transparent" />
                  <path d={barPath(x, y(point.orders), BAR, PLOT + TOP - y(point.orders))} fill={ALL_FILL} />
                  <path d={barPath(x, y(point.paidOrders), BAR, PLOT + TOP - y(point.paidOrders))} fill={PAID_FILL} />
                  <text x={x + BAR / 2} y={HEIGHT - 10} textAnchor="middle" fontSize={11} fill="currentColor">
                    {label(point, index)}
                  </text>
                </g>
              )
            })}
          </svg>
        </div>
      )}

      {points.length > 0 && (
        <details className="mt-2 text-sm">
          <summary className="cursor-pointer text-brand-700 underline">Show the numbers</summary>
          <table className="mt-2 w-full text-left text-sm">
            <caption className="sr-only">{`${title}: ${description}`}</caption>
            <thead>
              <tr className="text-slate-600">
                <th scope="col" className="py-1 font-medium">
                  Period
                </th>
                <th scope="col" className="py-1 text-right font-medium">
                  Orders
                </th>
                <th scope="col" className="py-1 text-right font-medium">
                  Paid
                </th>
              </tr>
            </thead>
            <tbody>
              {points.map((point, index) => (
                <tr key={point.label} className="border-t border-slate-100">
                  <th scope="row" className="py-1 font-normal">
                    {label(point, index)}
                  </th>
                  <td className="py-1 text-right tabular-nums">{point.orders}</td>
                  <td className="py-1 text-right tabular-nums">{point.paidOrders}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      )}
    </section>
  )
}
