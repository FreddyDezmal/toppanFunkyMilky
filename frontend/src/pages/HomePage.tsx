import { SystemStatusCard } from '../features/system-status/SystemStatusCard'

export function HomePage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Welcome</h1>
        <p className="mt-2 text-slate-600">
          Foundation build: this page confirms that the frontend can reach the API, and the API can reach the database.
        </p>
      </div>
      <SystemStatusCard />
    </div>
  )
}