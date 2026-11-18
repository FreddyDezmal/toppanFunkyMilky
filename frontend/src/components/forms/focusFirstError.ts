/** Moves keyboard focus to the first field (in form order) that has an error. Field ids equal field names. */
export function focusFirstError(fieldsInOrder: readonly string[], errors: object): void {
  const messages = errors as Record<string, string | undefined>
  const first = fieldsInOrder.find((field) => messages[field])
  if (first) {
    document.getElementById(first)?.focus()
  }
}