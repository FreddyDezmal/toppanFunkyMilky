import { Link } from 'react-router'
import { buttonClasses } from '../../components/ui/buttonClasses'
import { EmptyState } from '../../components/ui/EmptyState'

/** Shown on later steps when the draft has no milkshakes yet. */
export function EmptyDraft() {
  return (
    <section aria-labelledby="empty-draft-heading" className="mx-auto max-w-2xl">
      <h1 id="empty-draft-heading" className="text-3xl font-semibold text-chocolate">
        Your order is empty
      </h1>
      <div className="mt-6">
        <EmptyState
          title="No milkshakes yet"
          action={
            <Link to="/order" className={buttonClasses()}>
              Choose how many
            </Link>
          }
        >
          Start by choosing how many milkshakes you would like.
        </EmptyState>
      </div>
    </section>
  )
}
