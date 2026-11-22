import { Icon } from './Icon'

export interface StepperStep {
  label: string
}

interface StepperProps {
  steps: readonly StepperStep[]
  /** Index of the current step (0-based). Earlier steps show as done. */
  current: number
  /** Accessible name of the list, e.g. "Order progress". */
  label: string
}

/**
 * Progress through a fixed sequence. The current step carries aria-current="step"; done steps say so in text, so the
 * state never depends on colour. On narrow screens only "Step n of m: label" shows, so it never wraps.
 */
export function Stepper({ steps, current, label }: StepperProps) {
  const currentStep = steps[current]
  return (
    <nav aria-label={label}>
      <p className="text-sm font-semibold text-chocolate sm:hidden">
        Step {current + 1} of {steps.length}
        {currentStep ? `: ${currentStep.label}` : ''}
      </p>
      <div aria-hidden="true" className="mt-2 flex gap-1.5 sm:hidden">
        {steps.map((step, index) => (
          <span key={step.label} className={`h-1.5 flex-1 rounded-full ${index <= current ? 'bg-raspberry' : 'bg-line'}`} />
        ))}
      </div>
      <ol className="hidden items-center gap-2 sm:flex">
        {steps.map((step, index) => {
          const done = index < current
          const active = index === current
          return (
            <li key={step.label} className="flex items-center gap-2" aria-current={active ? 'step' : undefined}>
              <span
                className={`flex size-8 items-center justify-center rounded-full text-sm font-bold ${
                  done
                    ? 'bg-raspberry text-white'
                    : active
                      ? 'border-2 border-raspberry bg-surface text-raspberry'
                      : 'border border-line-strong bg-surface text-warm-grey'
                }`}
              >
                {done ? <Icon name="check" className="size-4" /> : index + 1}
              </span>
              <span className={`text-sm ${active ? 'font-semibold text-chocolate' : 'text-warm-grey'}`}>
                {step.label}
                {done && <span className="sr-only"> (done)</span>}
              </span>
              {index < steps.length - 1 && <span aria-hidden="true" className="mx-1 h-px w-6 bg-line-strong lg:w-10" />}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
