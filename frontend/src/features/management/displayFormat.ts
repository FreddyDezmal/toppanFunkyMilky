import { createContext, useContext } from 'react'
import type { Setting } from './types'

/** Locale, currency and business time zone from the system settings, shared by all management pages. */
export interface DisplayFormat {
  locale?: string
  currencyCode?: string
  /** The business time zone (IANA id) used for dates in reports. */
  timeZone?: string
  /** Lets the settings page apply a saved locale everywhere without reloading. */
  applySettings?: (settings: Setting[]) => void
}

export const DisplayFormatContext = createContext<DisplayFormat>({})

export function useDisplayFormat(): DisplayFormat {
  return useContext(DisplayFormatContext)
}