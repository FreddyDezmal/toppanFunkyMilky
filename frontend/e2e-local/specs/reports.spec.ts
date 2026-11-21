import { expect, test, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { payOrder, useTestCheckout } from '../support/providers'
import { expectNoSeriousAccessibilityProblems, managerPage, placeOrder, registerPatron } from '../support/stack'

/**
 * M13 E2E-11 (MR-10; FR-25, FR-26) on the local E2E stack: orders on a date, trends, and CSV exports that match the
 * screen. Runs in the "reports" project AFTER every other project (playwright.local.config.ts), so no other test is
 * placing orders while totals are compared.
 */
interface Csv {
  header: string[]
  rows: Record<string, string>[]
}

/** RFC 4180 CSV (quoted cells, doubled quotes, CRLF), with or without a byte-order mark. */
function parseCsv(text: string): Csv {
  const records: string[][] = []
  let record: string[] = []
  let cell = ''
  let quoted = false
  const input = text.replace(/^﻿/, '')
  for (let i = 0; i < input.length; i++) {
    const c = input[i]
    if (quoted) {
      if (c === '"' && input[i + 1] === '"') {
        cell += '"'
        i++
      } else if (c === '"') {
        quoted = false
      } else {
        cell += c
      }
    } else if (c === '"') {
      quoted = true
    } else if (c === ',') {
      record.push(cell)
      cell = ''
    } else if (c === '\n' || c === '\r') {
      if (c === '\r' && input[i + 1] === '\n') {
        i++
      }
      record.push(cell)
      records.push(record)
      record = []
      cell = ''
    } else {
      cell += c
    }
  }
  if (cell !== '' || record.length > 0) {
    record.push(cell)
    records.push(record)
  }
  const [header = [], ...rest] = records
  return { header, rows: rest.map((r) => Object.fromEntries(header.map((h, i) => [h, r[i] ?? '']))) }
}

/** Clicks an Export button and returns the downloaded CSV. */
async function exportCsv(page: Page, label: string): Promise<Csv> {
  const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: label }).click()])
  expect(download.suggestedFilename()).toMatch(/\.csv$/)
  return parseCsv(readFileSync((await download.path())!, 'utf8'))
}

const reportRow = (page: Page, reference: string) => page.getByRole('row').filter({ has: page.getByRole('link', { name: reference }) })

test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

test('E2E-11 reports: orders on a date with status filters, trends for the day, and exports that match the screen', async ({ page, browser }) => {
  test.setTimeout(180_000)
  const email = await registerPatron(page)
  const paid = await placeOrder(page)
  await payOrder(page, email)
  const unpaid = await placeOrder(page)

  const manager = await managerPage(browser)

  // One order on screen, found by reference, and its export: the same values, column for column.
  await manager.goto(`/manager/reports?q=${paid.reference}`)
  await expect(reportRow(manager, paid.reference)).toBeVisible()
  const cells = (await reportRow(manager, paid.reference).getByRole('cell').allTextContents()).map((t) => t.trim())
  const single = await exportCsv(manager, 'Export all matching orders as CSV')
  expect(single.header).toEqual(['Date', 'Time', 'Order', 'Milkshake', 'Flavour', 'Topping', 'Consistency', 'Restaurant', 'Pickup', 'Payment status', 'Refund'])
  expect(single.rows).toHaveLength(1)
  const exported = single.rows[0]!
  expect([
    exported.Date.replaceAll('-', '/'),
    exported.Time,
    exported.Order,
    exported.Flavour,
    exported.Topping,
    exported.Consistency,
    exported.Pickup.replaceAll('-', '/'),
    exported['Payment status'],
  ]).toEqual(cells)
  expect(exported['Payment status']).toBe('Paid')
  const today = exported.Date

  // Status filters on the day's orders.
  await manager.goto(`/manager/reports?from=${today}`)
  const statuses = manager.getByRole('group', { name: 'Show orders by payment status' })
  await statuses.getByRole('button', { name: 'Unpaid', exact: true }).click()
  await expect(statuses.getByRole('button', { name: 'Unpaid', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await expect(manager.getByText('Loading orders…')).toHaveCount(0)
  await expect(reportRow(manager, unpaid.reference)).toContainText('Unpaid')
  await expect(reportRow(manager, paid.reference)).toHaveCount(0)
  await statuses.getByRole('button', { name: 'Paid', exact: true }).click()
  await expect(reportRow(manager, paid.reference)).toContainText('Paid')
  await expect(reportRow(manager, unpaid.reference)).toHaveCount(0)
  await expectNoSeriousAccessibilityProblems(manager, 'Reports – Orders (Paid)')

  // Every order of the day, exported, against the day's trends on screen and in their own export.
  await statuses.getByRole('button', { name: 'All', exact: true }).click()
  await expect(manager.getByText('Loading orders…')).toHaveCount(0)
  const day = await exportCsv(manager, 'Export all matching orders as CSV')
  const orders = new Map(day.rows.map((r) => [r.Order, r['Payment status']]))
  expect(orders.get(paid.reference)).toBe('Paid')
  expect(orders.get(unpaid.reference)).toBe('Unpaid')
  const paidCount = [...orders.values()].filter((status) => status === 'Paid').length

  await manager.goto(`/manager/reports?tab=trends&from=${today}`)
  await expect(manager.getByText('Loading trends…')).toHaveCount(0)
  const totalsText = `${orders.size === 1 ? '1 order' : `${orders.size} orders`}, ${paidCount} paid.`
  await expect(manager.getByText(totalsText, { exact: false })).toBeVisible()
  await expect(manager.getByRole('heading', { name: 'Weekly' })).toBeVisible()

  const trends = await exportCsv(manager, 'Export the trends as CSV')
  expect(trends.header).toEqual(['Series', 'Period', 'Orders', 'Paid orders'])
  for (const series of ['Day of the week', 'Month', 'Year']) {
    const points = trends.rows.filter((r) => r.Series === series)
    expect(points.length, series).toBeGreaterThan(0)
    expect(points.reduce((sum, r) => sum + Number(r.Orders), 0), `${series}: orders`).toBe(orders.size)
    expect(points.reduce((sum, r) => sum + Number(r['Paid orders']), 0), `${series}: paid orders`).toBe(paidCount)
  }
  expect(trends.rows.filter((r) => r.Series === 'Day of the week')).toHaveLength(7)
  await expectNoSeriousAccessibilityProblems(manager, 'Reports – Trends')
  await manager.context().close()
})
