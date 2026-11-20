import type { AuditChange, SecurityEventType } from './types'

/** Lookup tables a manager maintains (URS §3.4.3), in the order of the management sections. */
export const LOOKUP_TABLES: { value: string; label: string }[] = [
  { value: 'flavours', label: 'Flavours' },
  { value: 'flavour_prices', label: 'Flavour prices' },
  { value: 'toppings', label: 'Toppings' },
  { value: 'topping_prices', label: 'Topping prices' },
  { value: 'consistencies', label: 'Consistencies' },
  { value: 'consistency_prices', label: 'Consistency prices' },
  { value: 'restaurants', label: 'Restaurants' },
  { value: 'system_settings', label: 'Settings' },
  { value: 'discount_tiers', label: 'Discount tiers' },
]

/** Every other audited table, for "All changes". */
export const OTHER_TABLES: { value: string; label: string }[] = [
  { value: 'orders', label: 'Orders' },
  { value: 'order_items', label: 'Order milkshakes' },
  { value: 'payments', label: 'Payments' },
  { value: 'payment_events', label: 'Payment notifications' },
  { value: 'refunds', label: 'Refunds' },
  { value: 'cancellation_requests', label: 'Cancellation requests' },
  { value: 'email_notifications', label: 'Emails' },
  { value: 'users', label: 'User accounts' },
  { value: 'user_roles', label: 'User roles' },
  { value: 'roles', label: 'Roles' },
]

export const TABLE_LABELS: Record<string, string> = Object.fromEntries([...LOOKUP_TABLES, ...OTHER_TABLES].map((t) => [t.value, t.label]))

export const ACTION_LABELS: Record<AuditChange['action'], string> = { Insert: 'Added', Update: 'Changed', Delete: 'Removed' }

export const ACTORS: { value: string; label: string }[] = [
  { value: 'user', label: 'A signed-in user' },
  { value: 'anonymous', label: 'An anonymous visitor' },
  { value: 'system:paystack-webhook', label: 'System: Paystack notification' },
  { value: 'system:payment-reconciliation', label: 'System: payment check' },
  { value: 'system:email-dispatcher', label: 'System: email sending' },
  { value: 'system:seeding', label: 'System: start-up data' },
  { value: 'system:manager-provisioning', label: 'System: manager set-up' },
]

export const EVENT_LABELS: Record<SecurityEventType, string> = {
  LoginSucceeded: 'Signed in',
  LoginFailed: 'Sign-in failed',
  LoginLockedOut: 'Locked out',
  Logout: 'Signed out',
}

/** Who made a change: the account for a signed-in user, otherwise the kind of actor; "Not recorded" for old records. */
export function describeActor(change: Pick<AuditChange, 'actor' | 'user'>): string {
  if (change.actor === null) {
    return change.user ?? 'Not recorded'
  }
  if (change.actor === 'user') {
    return change.user ?? 'A signed-in user'
  }
  return ACTORS.find((a) => a.value === change.actor)?.label ?? change.actor
}

/** A stored value as text: empty for null, "Hidden" for masked personal data, JSON for anything structured. */
export function describeValue(value: unknown): string {
  if (value === null || value === undefined || value === '') {
    return '(empty)'
  }
  if (value === '***') {
    return 'Hidden'
  }
  if (typeof value === 'string') {
    return value
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }
  return JSON.stringify(value)
}

/** "Flavours: Chocolate (#12)" when the change carries a name, otherwise "Flavours #12". */
export function describeRecord(change: AuditChange): string {
  const table = TABLE_LABELS[change.entity] ?? change.entity
  const name = change.changes.find((c) => c.field === 'name' || c.field === 'key')
  const value = name ? (name.new ?? name.old) : null
  return typeof value === 'string' && value !== '***' ? `${table}: ${value} (#${change.recordId})` : `${table} #${change.recordId}`
}
