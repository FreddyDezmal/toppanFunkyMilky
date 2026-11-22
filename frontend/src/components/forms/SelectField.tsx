import { Icon } from '../ui/Icon'

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
      <label htmlFor={id} className="block text-sm font-semibold text-chocolate">
        {label}
      </label>
      <div className="relative mt-1.5">
        <select
          id={id}
          name={id}
          value={value}
          disabled={disabled}
          aria-required={required ? 'true' : undefined}
          aria-invalid={error ? 'true' : undefined}
          aria-describedby={describedBy}
          onChange={(event) => onChange(event.target.value)}
          className={`block min-h-11 w-full appearance-none rounded-control border bg-surface py-2 pr-10 pl-3.5 text-base text-chocolate shadow-sm focus:outline-none focus:ring-2 disabled:bg-cream disabled:text-warm-grey ${
            error ? 'border-danger focus:ring-danger' : 'border-line-strong focus:border-raspberry focus:ring-raspberry'
          }`}
        >
          {placeholder !== undefined && <option value="">{placeholder}</option>}
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <Icon name="chevron-down" className="pointer-events-none absolute top-1/2 right-3.5 size-5 -translate-y-1/2 text-chocolate" />
      </div>
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