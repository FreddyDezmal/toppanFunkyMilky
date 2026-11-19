import { useOutletContext } from 'react-router'
import type { OrderingOptions } from './types'

/** What the ordering layout gives its pages: the options in force, loaded from the API. */
export interface OrderingContextValue {
  options: OrderingOptions
  /** Loads the options again and clears any selections that are no longer offered (decision E6). */
  reloadOptions: () => void
}

export function useOrdering(): OrderingContextValue {
  return useOutletContext<OrderingContextValue>()
}