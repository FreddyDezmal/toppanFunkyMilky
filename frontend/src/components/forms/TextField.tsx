interface TextFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  type?: 'text' | 'email' | 'password' | 'tel' | 'datetime-local'
  /** Keyboard hint for touch devices, e.g. "decimal" for prices. */
  inputMode?: 'text' | 'numeric' | 'decimal'
  autoComplete?: string
  hint?: string
  error?: string
  /** Announced as required to screen readers (default true). */
  required?: boolean
  disabled?: boolean
  /** Earliest allowed value for date/time inputs. */
  min?: string
    /** Latest allowed value for date/time inputs. */
  max?: string
}

/** Labelled input whose hint and error are announced by screen readers (aria-describedby, aria-invalid). */
export function TextField({
  id,
  label,
  value,
  onChange,
  type = 'text',
  inputMode,
  autoComplete,
  hint,
  error,
  required = true,
  disabled = false,
  min,
  max,
}: TextFieldProps) {
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-semibold text-chocolate">
        {label}
      </label>
      <input
        id={id}
        name={id}
        type={type}
        value={value}
        inputMode={inputMode}
        autoComplete={autoComplete}
        disabled={disabled}
        min={min}
        max={max}        
        aria-required={required ? 'true' : undefined}
        aria-invalid={error ? 'true' : undefined}
        aria-describedby={describedBy}
        onChange={(event) => onChange(event.target.value)}
        className={`mt-1.5 block min-h-11 w-full rounded-control border bg-surface px-3.5 py-2 text-base text-chocolate shadow-sm focus:outline-none focus:ring-2 disabled:bg-cream disabled:text-warm-grey ${
          error ? 'border-danger focus:ring-danger' : 'border-line-strong focus:border-raspberry focus:ring-raspberry'
        }`}
      />
      {hint && (
        <p id={hintId} className="mt-1.5 text-sm text-warm-grey">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="mt-1.5 text-sm font-medium text-danger">
          {error}
        </p>
      )}
    </div>
  )
}