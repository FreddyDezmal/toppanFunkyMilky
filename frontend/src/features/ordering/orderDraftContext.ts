import { createContext, useContext, type Dispatch } from 'react'
import type { DraftAction } from './draft'
import type { OrderDraft } from './types'

export interface OrderDraftContextValue {
  draft: OrderDraft
  dispatch: Dispatch<DraftAction>
}

export const OrderDraftContext = createContext<OrderDraftContextValue | null>(null)

export function useOrderDraft(): OrderDraftContextValue {
  const value = useContext(OrderDraftContext)
  if (!value) {
    throw new Error('useOrderDraft must be used inside <OrderDraftProvider>.')
  }
  return value
}