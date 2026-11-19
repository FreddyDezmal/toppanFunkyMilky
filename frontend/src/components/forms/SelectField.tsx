export interface SelectOption {
  value: string
  label: string
}

interface SelectFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  options: readonly SelectOption[]
  /** First, empty choice shown until something is selected, e.g. "Choose a flavour". */
  placeholder?: string
  hint?: string
  error?: string
  /** Announced as required to screen readers (default true). */
  required?: boolean
  disabled?: boolean
}

/** Labelled native select whose hint and error are announced by screen readers (aria-describedby, aria-invalid). */
export function SelectField({
  id,
  label,
  value,
  onChange,
  options,
  placeholder,
  hint,
  error,
  required = true,
  disabled = false,
}: SelectFieldProps) {
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-slate-700">
        {label}
      </label>
      <select
        id={id}
        name={id}
        value={value}
        disabled={disabled}
        aria-required={required ? 'true' : undefined}
        aria-invalid={error ? 'true' : undefined}
        aria-describedby={describedBy}
        onChange={(event) => onChange(event.target.value)}
        className={`mt-1 block w-full rounded-lg border bg-white px-3 py-2 text-sm shadow-sm focus:outline-none focus:ring-2 disabled:bg-slate-100 ${
          error ? 'border-rose-400 focus:ring-rose-300' : 'border-slate-300 focus:ring-brand-500'
        }`}
      >
        {placeholder !== undefined && <option value="">{placeholder}</option>}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
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