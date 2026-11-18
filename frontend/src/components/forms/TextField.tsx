interface TextFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  type?: 'text' | 'email' | 'password' | 'tel'
  autoComplete?: string
  hint?: string
  error?: string
}

/** Labelled input whose hint and error are announced by screen readers (aria-describedby, aria-invalid). */
export function TextField({ id, label, value, onChange, type = 'text', autoComplete, hint, error }: TextFieldProps) {
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-slate-700">
        {label}
      </label>
      <input
        id={id}
        name={id}
        type={type}
        value={value}
        autoComplete={autoComplete}
        aria-required="true"
        aria-invalid={error ? 'true' : undefined}
        aria-describedby={describedBy}
        onChange={(event) => onChange(event.target.value)}
        className={`mt-1 block w-full rounded-lg border px-3 py-2 text-sm shadow-sm focus:outline-none focus:ring-2 ${
          error ? 'border-rose-400 focus:ring-rose-300' : 'border-slate-300 focus:ring-brand-500'
        }`}
      />
      {hint && (
        <p id={hintId} className="mt-1 text-xs text-slate-500">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="mt-1 text-sm text-rose-700">
          {error}
        </p>
      )}
    </div>
  )
}