import { useEffect, useMemo, useReducer, type ReactNode } from 'react'
import { useAuth } from '../auth/authContext'
import { draftReducer, EMPTY_DRAFT } from './draft'
import { clearOrderDrafts, loadDraft, saveDraft } from './draftStorage'
import { OrderDraftContext, type OrderDraftContextValue } from './orderDraftContext'

/**
 * Holds the signed-in patron's draft. Remounts when the user changes (the `key`), so one user's draft is never
 * shown to another; drafts of other users in this tab are removed. Nothing is sent to the server from here.
 */
export function OrderDraftProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const userId = user?.id ?? null

  return (
    <DraftStore key={userId ?? 'signed-out'} userId={userId}>
      {children}
    </DraftStore>
  )
}

function DraftStore({ userId, children }: { userId: string | null; children: ReactNode }) {
  const [draft, dispatch] = useReducer(draftReducer, userId, (id: string | null) => (id ? loadDraft(id) : EMPTY_DRAFT))

  useEffect(() => {
    if (userId) {
      clearOrderDrafts(userId)
    }
  }, [userId])

  useEffect(() => {
    if (userId) {
      saveDraft(userId, draft)
    }
  }, [userId, draft])

  const value = useMemo<OrderDraftContextValue>(() => ({ draft, dispatch }), [draft])

  return <OrderDraftContext value={value}>{children}</OrderDraftContext>
}