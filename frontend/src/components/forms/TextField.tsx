import { useEffect, useRef, useState } from 'react'
import { Icon } from '../ui/Icon'

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

/**
 * Labelled input whose hint and error are announced by screen readers (aria-describedby, aria-invalid).
 * Password fields get a Show/Hide button so people can check what they typed; the password is hidden again when the
 * form is submitted.
 */
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
  const isPassword = type === 'password'
  const [revealed, setRevealed] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  // Hide the password again when its form is submitted, so it is not left on screen.
  useEffect(() => {
    const form = inputRef.current?.form
    if (!isPassword || !form) {
      return undefined
    }
    const hide = () => setRevealed(false)
    form.addEventListener('submit', hide)
    return () => form.removeEventListener('submit', hide)
  }, [isPassword])

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-semibold text-chocolate">
        {label}
      </label>
      <div className="relative mt-1.5">
        <input
          ref={inputRef}
          id={id}
          name={id}
          type={isPassword && revealed ? 'text' : type}
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
          className={`block min-h-11 w-full rounded-control border bg-surface py-2 pl-3.5 text-base ${isPassword ? 'pr-24' : 'pr-3.5'} text-chocolate shadow-sm focus:outline-none focus:ring-2 disabled:bg-cream disabled:text-warm-grey ${
            error ? 'border-danger focus:ring-danger' : 'border-line-strong focus:border-raspberry focus:ring-raspberry'
          }`}
        />
        {isPassword && (
          <button
            type="button"
            onClick={() => setRevealed((shown) => !shown)}
            disabled={disabled}
            aria-controls={id}
            // The name starts with the visible word (WCAG 2.5.3) and says what will happen.
            aria-label={`${revealed ? 'Hide' : 'Show'} ${label.toLowerCase()}`}
            className="absolute top-1/2 right-1.5 flex min-h-9 -translate-y-1/2 items-center gap-1.5 rounded-full px-3 text-sm font-semibold text-raspberry hover:bg-strawberry-wash disabled:opacity-50"
          >
            <Icon name={revealed ? 'eye-off' : 'eye'} className="size-4" />
            {revealed ? 'Hide' : 'Show'}
          </button>
        )}
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