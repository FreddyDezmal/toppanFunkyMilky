import { expect, test } from '@playwright/test'
import { useTestCheckout } from '../support/providers'
import { registerPatron } from '../support/stack'

/**
 * M14 DEF-04 regression: "Continue to pickup" must not move when the live estimate arrives. Before the fix the price
 * table appeared above the button and pushed it down about 110 px, so a click at that moment missed. The quote is held
 * back for 3 seconds so the position can be measured while the estimate is still loading, then again once it is shown.
 */
test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

test('DEF-04 the Continue to pickup button stays put when the estimate arrives', async ({ page }) => {
  await registerPatron(page)
  await page.route('**/api/ordering/quote', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 3_000))
    await route.continue()
  })

  await page.goto('/order')
  await page.getByLabel('Number of milkshakes').selectOption('1')
  await page.getByRole('button', { name: 'Continue' }).click()
  const card = page.getByRole('listitem', { name: 'Milkshake 1', exact: true })
  for (const field of ['Flavour', 'Consistency', 'Topping']) {
    await card.getByLabel(field, { exact: true }).selectOption({ index: 1 })
  }
  await card.getByRole('button', { name: 'Done with milkshake 1' }).click()

  const status = page.getByRole('region', { name: 'Estimated price' }).getByRole('status')
  const button = page.getByRole('button', { name: 'Continue to pickup' })
  await expect(status).toHaveText('Updating prices…')
  const before = await button.boundingBox()

  await expect(status).toHaveText(/^Estimated total .+\.$/, { timeout: 15_000 })
  const after = await button.boundingBox()

  expect(before).not.toBeNull()
  expect(after).not.toBeNull()
  expect(Math.abs(after!.y - before!.y), `moved from y=${before!.y} to y=${after!.y}`).toBeLessThanOrEqual(1)
  expect(Math.abs(after!.x - before!.x)).toBeLessThanOrEqual(1)

  // And the click lands: the patron reaches the pickup step.
  await button.click()
  await expect(page.getByLabel('Pickup restaurant')).toBeVisible()
})
