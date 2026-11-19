import { createContext, useContext } from 'react'

/** Locale and currency from the system settings, shared by all management pages. */
export interface DisplayFormat {
  locale?: string
  currencyCode?: string
}

export const DisplayFormatContext = createContext<DisplayFormat>({})

export function useDisplayFormat(): DisplayFormat {
  return useContext(DisplayFormatContext)
}