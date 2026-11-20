import { useState } from 'react'
import type { DownloadedFile } from '../../lib/api/httpClient'
import { saveFile } from '../../lib/download'
import { describeReportError } from './reports'

const BUTTON_CLASS = 'rounded-lg px-3 py-1.5 text-sm font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:opacity-50'

interface ExportButtonProps {
  /** Downloads the CSV for what is on screen (all pages, at most 10,000 records). */
  run: () => Promise<DownloadedFile>
  /** Used when the server names no file. */
  fallbackName: string
  /** Accessible name, e.g. "Export orders as CSV". */
  label: string
}

/**
 * Export (URS §3.4.4): asks the server for a CSV of the current filters and saves it. One request at a time; the
 * outcome is announced (saved, or why not, e.g. more than 10,000 records).
 */
export function ExportButton({ run, fallbackName, label }: ExportButtonProps) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null)

  async function onClick() {
    if (busy) {
      return
    }
    setBusy(true)
    setMessage(null)
    try {
      const file = await run()
      const name = file.fileName ?? fallbackName
      saveFile(file.blob, name)
      setMessage({ text: `Saved ${name}.`, isError: false })
    } catch (error: unknown) {
      setMessage({ text: describeReportError(error), isError: true })
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <button type="button" aria-label={label} disabled={busy} aria-busy={busy} onClick={() => void onClick()} className={BUTTON_CLASS}>
        {busy ? 'Exporting…' : 'Export'}
      </button>
      {message && (
        <span role={message.isError ? 'alert' : 'status'} className={`text-sm ${message.isError ? 'text-rose-700' : 'text-emerald-800'}`}>
          {message.text}
        </span>
      )}
    </span>
  )
}
