/** Placeholder block shown while content loads. Decorative: pair it with a status message for screen readers. */
export function Skeleton({ className = '' }: { className?: string }) {
  return <span aria-hidden="true" className={`block animate-pulse rounded-md bg-strawberry-tint/70 ${className}`.trim()} />
}
