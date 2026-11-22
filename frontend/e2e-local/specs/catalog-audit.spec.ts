import { expect, test, type Page } from '@playwright/test'
import { mailMessage, payOrder, paystack, useTestCheckout, waitForMail } from '../support/providers'
import { centsOf, expectNoSeriousAccessibilityProblems, managerPage, placeOrder, registerPatron, reviewOrder, setQuantity } from '../support/stack'

/**
 * M13 E2E-08, E2E-09 and E2E-10 (MR-02, MR-03, MR-04; FR-09, FR-21 to FR-23, FR-27) on the local E2E stack.
 * The flavour these tests change is their own: created with display order 9999 so it is listed last (other tests pick
 * the first option), and deactivated at the end (records are never deleted).
 */
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
const amount = (whole: number) => String.raw`R\s?${whole}[.,]\d{2}`

test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

async function addFlavour(manager: Page, name: string, price: string): Promise<void> {
  await manager.goto('/manager/catalog/flavours')
  await manager.getByRole('button', { name: 'Add flavour' }).click()
  await manager.getByLabel('Name', { exact: true }).fill(name)
  await manager.getByLabel('Display order', { exact: true }).fill('9999')
  await manager.getByLabel('Price', { exact: true }).fill(price)
  await manager.getByRole('button', { name: 'Save' }).click()
  await expect(manager.getByText(`Added “${name}”.`)).toBeVisible()
}

async function deactivateFlavour(manager: Page, name: string): Promise<void> {
  await manager.goto('/manager/catalog/flavours')
  await manager.getByRole('button', { name: `Deactivate ${name}` }).click()
  await manager.getByRole('alertdialog').getByRole('button', { name: 'Deactivate' }).click()
  await expect(manager.getByText(`“${name}” is hidden from new orders. Its history is kept.`)).toBeVisible()
}

/** Reports → Audit Lookup, in the given view, searched for `q` (optionally one table and one user's changes). */
async function auditSearch(manager: Page, view: 'Lookup changes' | 'All changes', q: string, table?: string, user?: string) {
  await manager.goto('/manager/reports?tab=audit')
  await expect(manager.getByRole('heading', { name: 'Audit history' })).toBeVisible()
  await manager.getByRole('group', { name: 'Audit view' }).getByRole('button', { name: view }).click()
  const form = manager.getByRole('form', { name: view === 'Lookup changes' ? 'Filter lookup changes' : 'Filter all changes' })
  if (table) {
    await form.getByLabel(view === 'Lookup changes' ? 'Lookup table' : 'Table').selectOption({ label: table })
  }
  if (user) {
    await form.getByLabel('User email').fill(user)
  }
  await form.getByLabel('Search changes').fill(q)
  await form.getByRole('button', { name: 'Show' }).click()
  await expect(manager.getByText('Loading changes…')).toHaveCount(0)
  return manager.getByRole('list', { name: view === 'Lookup changes' ? 'Lookup changes' : 'Changes' })
}

/** The From and To cells of a field in one audit entry. */
function fieldChange(entry: ReturnType<Page['locator']>, field: string) {
  return entry.getByRole('row', { name: new RegExp(`^${escape(field)}\\b`) })
}

test('E2E-08 and E2E-09 a manager changes a flavour price and renames it: only new quotes change, and Audit Lookup shows who changed what from what to what', async ({
  page,
  browser,
}) => {
  test.setTimeout(240_000)
  const name = `ZZ E2E Flavour ${Date.now()}`
  const renamed = `${name} renamed`
  const manager = await managerPage(browser)
  const managerEmail = process.env.E2E_LOCAL_MANAGER_EMAIL ?? ''

  await test.step('the manager adds a flavour at R 12,50', () => addFlavour(manager, name, '12,50'))

  // A patron orders and pays at the old price.
  const email = await registerPatron(page)
  const order = await placeOrder(page, { flavour: name })
  await expect(page.getByText(new RegExp(`^${escape(name)} · ${amount(12)}$`))).toBeVisible()
  await payOrder(page, email)
  const receipt = await mailMessage((await waitForMail(email, `Order ${order.reference} confirmed – your receipt`)).ID)
  expect(receipt.Text).toMatch(new RegExp(`${escape(name)} \\(${amount(12)}\\)`))

  await test.step('the manager changes the price to R 20,00 immediately', async () => {
    await manager.goto('/manager/catalog/flavours')
    await manager.getByRole('link', { name: `Prices for ${name}` }).click()
    await manager.getByLabel('New price').fill('20')
    await manager.getByLabel('Immediately').check()
    await manager.getByRole('button', { name: 'Change price now' }).click()
    await expect(manager.getByText('The new price applies from now.')).toBeVisible()
  })

  await test.step('a new quote uses the new price', async () => {
    await page.goto('/order')
    await setQuantity(page, 1)
    await page.getByRole('button', { name: 'Continue' }).click()
    const flavours = page.getByRole('listitem', { name: 'Milkshake 1', exact: true }).getByRole('group', { name: 'Flavour', exact: true })
    await expect(flavours.getByRole('radio', { name: new RegExp(`^${escape(name)},\\s?${amount(20)}$`) })).toHaveCount(1)
    const newTotal = await reviewOrder(page, { flavour: name })
    expect(centsOf(newTotal)).toBeGreaterThan(centsOf(order.totalText))
  })

  await test.step('the placed order keeps the price it was placed at', async () => {
    await page.goto(`/orders/${order.reference}`)
    await expect(page.getByText(new RegExp(`^${escape(name)} · ${amount(12)}$`))).toBeVisible()
    await expect(page.getByText(order.totalText, { exact: true }).first()).toBeVisible()
  })

  await test.step('the manager renames the flavour; the order and its report row keep the name it was ordered with', async () => {
    await manager.goto('/manager/catalog/flavours')
    await manager.getByRole('button', { name: `Edit ${name}` }).click()
    await manager.getByLabel('Name', { exact: true }).fill(renamed)
    await manager.getByRole('button', { name: 'Save' }).click()
    await expect(manager.getByText(`Saved changes to “${renamed}”.`)).toBeVisible()

    await page.reload()
    await expect(page.getByText(new RegExp(`^${escape(name)} · ${amount(12)}$`))).toBeVisible()
    await manager.goto(`/manager/reports?q=${order.reference}`)
    const reportRow = manager.getByRole('row').filter({ has: manager.getByRole('link', { name: order.reference }) })
    await expect(reportRow).toContainText(name)
    await expect(reportRow).not.toContainText('renamed')
  })

  await test.step('Audit Lookup: the rename and the new price, made by the manager', async () => {
    const renames = await auditSearch(manager, 'Lookup changes', 'renamed', 'Flavours')
    const rename = renames.getByRole('listitem').filter({ hasText: `Flavours: ${renamed}` }).first()
    await expect(rename).toContainText('Changed ·')
    await expect(rename).toContainText(`by ${managerEmail}`)
    await expect(fieldChange(rename, 'name')).toContainText(name)
    await expect(fieldChange(rename, 'name')).toContainText(renamed)
    await expectNoSeriousAccessibilityProblems(manager, 'Audit Lookup – lookup changes')

    // Both prices of this flavour were set by the manager: R 12,50 when it was added, then R 20,00.
    const prices = await auditSearch(manager, 'Lookup changes', '', 'Flavour prices', managerEmail)
    await expect(prices.getByRole('cell', { name: /^20([.,]0+)?$/ }).first()).toBeVisible()
    await expect(prices.getByRole('cell', { name: /^12[.,]50*$/ }).first()).toBeVisible()
    await expect(prices.getByRole('listitem').first()).toContainText(`by ${managerEmail}`)
  })

  await test.step('clean-up: the flavour is hidden from new orders', () => deactivateFlavour(manager, renamed))
  await manager.context().close()
})

test('E2E-10 the audit trail of a paid and refunded order shows each change and who or what made it, with the patron masked', async ({
  page,
  browser,
}) => {
  const email = await registerPatron(page)
  const order = await placeOrder(page)
  const tx = await payOrder(page, email)
  await page.getByRole('button', { name: 'Cancel order' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel order' }).click()
  await expect(page.getByText('Your order has been cancelled.')).toBeVisible()
  expect((await paystack.settleRefunds(tx.reference, 'processed')).statuses).toEqual([200])
  await page.reload()
  await expect(page.getByText(/– completed/)).toBeVisible()

  const manager = await managerPage(browser)

  // The order's own row, found by its reference; its record id then finds every change that names the order.
  const byReference = await auditSearch(manager, 'All changes', order.reference, 'Orders')
  const created = byReference.getByRole('listitem').filter({ hasText: /^Added · Orders #/ }).first()
  await expect(created).toBeVisible()
  const orderId = /Orders #(\S+?)\)?$/m.exec((await created.locator('p').first().textContent()) ?? '')?.[1] ?? ''
  expect(orderId).not.toBe('')

  const trail = await auditSearch(manager, 'All changes', orderId)
  const entries = trail.getByRole('listitem')
  await expect(entries.filter({ hasText: /^Added · Orders #/ })).toHaveCount(1)
  await expect(entries.filter({ hasText: /^Added · Payments #/ }).first()).toBeVisible()
  await expect(entries.filter({ hasText: /^Added · Refunds #/ })).toHaveCount(1)
  const statusChanges = entries.filter({ hasText: /^Changed · Orders #/ })
  await expect(statusChanges.filter({ has: manager.getByRole('cell', { name: 'Confirmed', exact: true }) }).first()).toBeVisible()
  await expect(statusChanges.filter({ has: manager.getByRole('cell', { name: 'Cancelled', exact: true }) }).first()).toBeVisible()
  // Personal data is masked: the patron's email never appears in full.
  await expect(trail).not.toContainText(email)
  await expectNoSeriousAccessibilityProblems(manager, 'Audit Lookup – all changes')

  // The refund was completed by the system (the verified Paystack notification), not by a person.
  const refundRow = entries.filter({ hasText: /^Added · Refunds #/ }).first()
  const refundId = /Refunds #(\S+?)\)?$/m.exec((await refundRow.locator('p').first().textContent()) ?? '')?.[1] ?? ''
  const refundChanges = await auditSearch(manager, 'All changes', refundId, 'Refunds')
  const processed = refundChanges.getByRole('listitem').filter({ has: manager.getByRole('cell', { name: 'Processed', exact: true }) }).first()
  await expect(processed).toContainText('by System: Paystack notification')

  // Audit history cannot be changed through the API: there is no way to edit or delete it.
  for (const method of ['PUT', 'PATCH', 'DELETE'] as const) {
    const response = await manager.request.fetch('/api/manager/audit/changes', { method })
    expect(response.status(), `${method} /api/manager/audit/changes`).toBeGreaterThanOrEqual(400)
  }
  await manager.context().close()
})
