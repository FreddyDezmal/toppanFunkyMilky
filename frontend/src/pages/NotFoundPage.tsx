import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <section className="rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
      <h1 className="text-2xl font-bold tracking-tight">Page not found</h1>
      <Link to="/" className="mt-4 inline-block font-medium text-brand-700 hover:underline">
        Back to the home page
      </Link>
    </section>
  )
}