import { EMPTY_DRAFT, EMPTY_DRINK, isDraftEmpty, MAX_DRAFT_DRINKS } from './draft'
import type { DrinkSelection, OrderDraft } from './types'

/**
 * The draft is mirrored to sessionStorage (decision E2) so a refresh does not lose it. It holds identifiers and the
 * pickup time only, is keyed by the signed-in user's id, ends with the browser tab and is cleared on sign-out.
 * Everything read back is treated as untrusted and rebuilt field by field.
 */
const KEY_PREFIX = 'milkshake.orderDraft.v1:'
const SUBMISSION_PREFIX = 'milkshake.orderSubmission.v1:'
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

/** Removes every stored draft and submission key in this tab, optionally keeping one user's. */
export function clearOrderDrafts(exceptUserId?: string): void {
  safely(() => {
    const keep =
      exceptUserId === undefined ? [] : [draftStorageKey(exceptUserId), `${SUBMISSION_PREFIX}${exceptUserId}`]
    const keys = Array.from({ length: sessionStorage.length }, (_, index) => sessionStorage.key(index))
    for (const key of keys) {
      if (key && (key.startsWith(KEY_PREFIX) || key.startsWith(SUBMISSION_PREFIX)) && !keep.includes(key)) {
        sessionStorage.removeItem(key)
      }
    }
  })
}

/** The content that identifies a submission: restaurant, pickup time and each milkshake's choices, in order. */
function submissionContent(draft: OrderDraft): string {
  return JSON.stringify([
    draft.restaurantId,
    draft.pickupLocal,
    draft.drinks.map((drink) => [drink.flavourId, drink.consistencyId, drink.toppingId]),
  ])
}

/**
 * The Idempotency-Key for placing this draft (M7, decision O5). The same draft content always gets the same key, also
 * after a timeout or a page refresh, so a retry can never create a second order; any change to the content gets a new
 * key, so a different order is never sent under an old key.
 */
export function orderSubmissionKey(userId: string, draft: OrderDraft): string {
  const storageKey = `${SUBMISSION_PREFIX}${userId}`
  const content = submissionContent(draft)
  const stored = safely(() => sessionStorage.getItem(storageKey))
  try {
    const parsed = stored ? (JSON.parse(stored) as unknown) : null
    if (isRecord(parsed) && parsed.content === content && typeof parsed.key === 'string' && UUID.test(parsed.key)) {
      return parsed.key
    }
  } catch {
    // Unreadable: start a new submission below.
  }
  const key = crypto.randomUUID()
  safely(() => sessionStorage.setItem(storageKey, JSON.stringify({ content, key })))
  return key
}

/** Forgets the submission key: after the order was placed, or when the server refused the key. */
export function forgetOrderSubmissionKey(userId: string): void {
  safely(() => sessionStorage.removeItem(`${SUBMISSION_PREFIX}${userId}`))
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

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