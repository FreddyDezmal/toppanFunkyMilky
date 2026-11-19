import { Link } from 'react-router'

/** Shown on later steps when the draft has no milkshakes yet. */
export function EmptyDraft() {
  return (
    <section aria-labelledby="empty-draft-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="empty-draft-heading" className="text-2xl font-bold tracking-tight">
        Your order is empty
      </h1>
      <p className="mt-2 text-slate-600">Start by choosing how many milkshakes you would like.</p>
      <Link
        to="/order"
        className="mt-4 inline-block rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
      >
        Choose how many
      </Link>
    </section>
  )
}