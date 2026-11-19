import { EMPTY_DRAFT, EMPTY_DRINK, isDraftEmpty, MAX_DRAFT_DRINKS } from './draft'
import type { DrinkSelection, OrderDraft } from './types'

/**
 * The draft is mirrored to sessionStorage (decision E2) so a refresh does not lose it. It holds identifiers and the
 * pickup time only, is keyed by the signed-in user's id, ends with the browser tab and is cleared on sign-out.
 * Everything read back is treated as untrusted and rebuilt field by field.
 */
const KEY_PREFIX = 'milkshake.orderDraft.v1:'
const LOCAL_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/

export function draftStorageKey(userId: string): string {
  return `${KEY_PREFIX}${userId}`
}

/** The stored draft for this user, or an empty draft when there is none or it cannot be read. */
export function loadDraft(userId: string): OrderDraft {
  const raw = safely(() => sessionStorage.getItem(draftStorageKey(userId)))
  if (!raw) {
    return EMPTY_DRAFT
  }
  try {
    return sanitizeDraft(JSON.parse(raw) as unknown)
  } catch {
    return EMPTY_DRAFT
  }
}

/** Stores the draft for this user; an empty draft removes the entry. Storage failures are ignored. */
export function saveDraft(userId: string, draft: OrderDraft): void {
  safely(() => {
    if (isDraftEmpty(draft)) {
      sessionStorage.removeItem(draftStorageKey(userId))
    } else {
      sessionStorage.setItem(draftStorageKey(userId), JSON.stringify(sanitizeDraft(draft)))
    }
  })
}

/** Removes every stored draft in this tab, optionally keeping one user's. */
export function clearOrderDrafts(exceptUserId?: string): void {
  safely(() => {
    const keep = exceptUserId === undefined ? undefined : draftStorageKey(exceptUserId)
    const keys = Array.from({ length: sessionStorage.length }, (_, index) => sessionStorage.key(index))
    for (const key of keys) {
      if (key?.startsWith(KEY_PREFIX) && key !== keep) {
        sessionStorage.removeItem(key)
      }
    }
  })
}

/** Rebuilds a draft from untrusted data, keeping only well-formed identifiers and pickup time. */
export function sanitizeDraft(value: unknown): OrderDraft {
  if (!isRecord(value)) {
    return EMPTY_DRAFT
  }
  const drinks = Array.isArray(value.drinks) ? value.drinks.slice(0, MAX_DRAFT_DRINKS).map(sanitizeDrink) : []
  return {
    restaurantId: toId(value.restaurantId),
    pickupLocal: typeof value.pickupLocal === 'string' && LOCAL_TIME.test(value.pickupLocal) ? value.pickupLocal : null,
    drinks,
  }
}

function sanitizeDrink(value: unknown): DrinkSelection {
  if (!isRecord(value)) {
    return { ...EMPTY_DRINK }
  }
  return { flavourId: toId(value.flavourId), consistencyId: toId(value.consistencyId), toppingId: toId(value.toppingId) }
}

function toId(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** sessionStorage can throw (disabled storage, private modes, quota): the draft then simply is not kept. */
function safely<T>(action: () => T): T | undefined {
  try {
    return action()
  } catch {
    return undefined
  }
}