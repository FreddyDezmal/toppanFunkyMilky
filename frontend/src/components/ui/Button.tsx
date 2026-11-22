import type { ComponentPropsWithRef, ReactNode } from 'react'
import { buttonClasses, type ButtonStyleOptions } from './buttonClasses'

interface ButtonProps extends ComponentPropsWithRef<'button'>, ButtonStyleOptions {
  /** Optional icon shown before the label (decorative: give the button a text label). */
  icon?: ReactNode
}

/** The one button. Defaults to type="button" so it never submits a form by accident. */
export function Button({
  variant,
  size,
  fullWidth,
  className,
  icon,
  type = 'button',
  children,
  ...rest
}: ButtonProps) {
  return (
    <button type={type} className={buttonClasses({ variant, size, fullWidth, className })} {...rest}>
      {icon}
      {children}
    </button>
  )
}
