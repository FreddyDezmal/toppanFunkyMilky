import type { ReactNode } from 'react'

/** Shared page frame: header, content area, footer. */
export function AppLayout({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-brand-50 text-slate-900">
      <header className="border-b border-brand-100 bg-white">
        <div className="mx-auto flex max-w-4xl items-center justify-between px-4 py-4">
          <span className="text-xl font-extrabold tracking-tight text-brand-600">Milky Shaky</span>
          <span className="text-sm text-slate-500">Order milkshakes for pickup</span>
        </div>
      </header>
      <main className="mx-auto max-w-4xl px-4 py-10">{children}</main>
      <footer className="mx-auto max-w-4xl px-4 pb-8 text-xs text-slate-400">Development build</footer>
    </div>
  )
}