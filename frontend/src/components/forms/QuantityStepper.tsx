import { Icon } from '../ui/Icon'

interface QuantityStepperProps {
  id: string
  label: string
  value: number
  min: number
  max: number
  onChange: (value: number) => void
  hint?: string
  error?: string
  /** Word used in the button names, e.g. "milkshakes" gives "Remove one milkshake"/"Add one milkshake". */
  unit?: { one: string }
}

/**
 * Whole-number input with large − and + buttons, limited to [min, max]. The number field itself stays editable and
 * labelled, so typing, arrow keys and screen readers all work; the buttons are a convenience.
 */
export function QuantityStepper({ id, label, value, min, max, onChange, hint, error, unit }: QuantityStepperProps) {
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined
  const clamp = (next: number) => Math.min(max, Math.max(min, next))
  const thing = unit?.one ?? 'item'

  return (
    <div>
      <label htmlFor={id} className="block text-sm font-semibold text-chocolate">
        {label}
      </label>
      <div className="mt-2 inline-flex items-center gap-2">
        <button
          type="button"
          onClick={() => onChange(clamp(value - 1))}
          disabled={value <= min}
          aria-controls={id}
          className="flex size-12 items-center justify-center rounded-full border border-line-strong bg-surface text-chocolate hover:bg-strawberry-wash disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Icon name="minus" label={`Remove one ${thing}`} />
        </button>
        <input
          id={id}
          name={id}
          type="number"
          inputMode="numeric"
          min={min}
          max={max}
          step={1}
          value={value}
          aria-invalid={error ? 'true' : undefined}
          aria-describedby={describedBy}
          onChange={(event) => {
            const next = Number.parseInt(event.target.value, 10)
            if (!Number.isNaN(next)) {
              onChange(clamp(next))
            }
          }}
          className="h-12 w-20 rounded-control border border-line-strong bg-surface text-center font-display text-2xl font-semibold text-chocolate focus:outline-none focus:ring-2 focus:ring-raspberry"
        />
        <button
          type="button"
          onClick={() => onChange(clamp(value + 1))}
          disabled={value >= max}
          aria-controls={id}
          className="flex size-12 items-center justify-center rounded-full bg-raspberry text-white hover:bg-raspberry-dark disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Icon name="plus" label={`Add one ${thing}`} />
        </button>
      </div>
      {hint && (
        <p id={hintId} className="mt-2 text-sm text-warm-grey">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="mt-2 text-sm font-medium text-danger">
          {error}
        </p>
      )}
    </div>
  )
}
