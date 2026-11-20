import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import { describeActor, describeValue } from './audit'
import { nextDay, startOfDayUtc } from './reports'
import type { AuditChange, AuditPage, SecurityEvent } from './types'

const CHANGES = 'GET /api/manager/audit/changes'
const SECURITY = 'GET /api/manager/audit/security'

function change(overrides: Partial<AuditChange> = {}): AuditChange {
  return {
    id: 501,
    occurredAt: '2026-11-20T14:40:00+00:00',
    action: 'Update',
    entity: 'flavours',
    recordId: '12',
    actor: 'user',
    userId: '01a1ea38-0000-7000-8000-000000000002',
    user: 'manager@milkyshaky.test',
    changes: [
      { field: 'name', old: 'Choc', new: 'Chocolate' },
      { field: 'display_order', old: 1, new: 2 },
    ],
    traceId: null,
    ...overrides,
  }
}

function page<T>(items: T[], nextCursor: string | null = null): AuditPage<T> {
  return { items, nextCursor }
}

function lastQuery(fetchMock: ReturnType<typeof renderManagerAt>, path: string): URLSearchParams {
  return new URL(String(callsTo(fetchMock, 'GET', path).at(-1)?.[0]), 'http://localhost').searchParams
}

beforeEach(() => clearCsrfToken())
afterEach(() => vi.unstubAllGlobals())

describe('Management → Reports → Audit Lookup', () => {
  it('shows who changed which lookup value from what to what, and when', async () => {
    const fetchMock = renderManagerAt('/manager/reports?tab=audit', { [CHANGES]: () => jsonResponse(page([change()])) })

    const list = await screen.findByRole('list', { name: 'Lookup changes' })
    expect(screen.getByRole('tab', { name: 'Audit Lookup' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('No date chosen: showing all changes, newest first.')).toBeInTheDocument()
    const item = within(list).getAllByRole('listitem')[0]
    expect(item).toHaveTextContent('Changed · Flavours: Chocolate (#12)')
    expect(item).toHaveTextContent('by manager@milkyshaky.test')
    expect(within(item).getAllByRole('row').slice(1).map((row) => row.textContent)).toEqual(['nameChocChocolate', 'display_order12'])

    const query = lastQuery(fetchMock, '/api/manager/audit/changes')
    expect([...query.entries()]).toEqual([['entity', 'flavours']])
  })

  it('switches lookup table, and limits history to the shared dates as UTC instants', async () => {
    const fetchMock = renderManagerAt('/manager/reports?tab=audit&from=2026-11-20', {
      [CHANGES]: () => jsonResponse(page([change({ entity: 'system_settings', recordId: 'Tax.VatRate', changes: [{ field: 'value', old: '0.15', new: '0.16' }] })])),
    })

    await screen.findByRole('list', { name: 'Lookup changes' })
    let query = lastQuery(fetchMock, '/api/manager/audit/changes')
    expect(query.get('from')).toBe(startOfDayUtc('2026-11-20'))
    expect(query.get('to')).toBe(startOfDayUtc('2026-11-21'))

    await userEvent.selectOptions(screen.getByLabelText('Lookup table'), 'Settings')
    await userEvent.click(screen.getByRole('button', { name: 'Show' }))

    await waitFor(() => expect(lastQuery(fetchMock, '/api/manager/audit/changes').get('entity')).toBe('system_settings'))
    query = lastQuery(fetchMock, '/api/manager/audit/changes')
    expect(query.get('from')).toBe(startOfDayUtc('2026-11-20'))
    expect(await screen.findByText('Changed · Settings #Tax.VatRate')).toBeInTheDocument()
  })

  it('filters all changes by table, action, actor, user and text, and loads more', async () => {
    let calls = 0
    const fetchMock = renderManagerAt('/manager/reports?tab=audit', {
      [CHANGES]: () => {
        calls += 1
        return calls < 3
          ? jsonResponse(page([change({ id: calls })], 'cursor-1'))
          : jsonResponse(page([change({ id: 99, entity: 'orders', recordId: '7', action: 'Insert', actor: null, user: null, changes: [] })]))
      },
    })

    await screen.findByRole('list', { name: 'Lookup changes' })
    await userEvent.click(screen.getByRole('button', { name: 'All changes' }))
    await screen.findByRole('list', { name: 'Changes' })
    expect([...lastQuery(fetchMock, '/api/manager/audit/changes').keys()]).toEqual([])

    await userEvent.selectOptions(screen.getByLabelText('Table'), 'Orders')
    await userEvent.selectOptions(screen.getByLabelText('Action'), 'Added')
    await userEvent.selectOptions(screen.getByLabelText('Made by'), 'System: payment check')
    await userEvent.type(screen.getByLabelText('User email'), 'thandi@example.test')
    await userEvent.type(screen.getByLabelText('Search changes'), 'Cancelled')
    await userEvent.click(screen.getByRole('button', { name: 'Show' }))

    await waitFor(() => expect(callsTo(fetchMock, 'GET', '/api/manager/audit/changes')).toHaveLength(3))
    const query = lastQuery(fetchMock, '/api/manager/audit/changes')
    expect(Object.fromEntries(query)).toEqual({
      entity: 'orders',
      action: 'Insert',
      actor: 'system:payment-reconciliation',
      user: 'thandi@example.test',
      q: 'Cancelled',
    })
    expect(await screen.findByText('Added · Orders #7')).toBeInTheDocument()
    expect(screen.getByText(/by Not recorded/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load more changes' })).not.toBeInTheDocument()
  })

  it('loads the next page with the cursor and keeps what is shown', async () => {
    let calls = 0
    const fetchMock = renderManagerAt('/manager/reports?tab=audit', {
      [CHANGES]: () => {
        calls += 1
        return calls === 1 ? jsonResponse(page([change()], 'next+1')) : jsonResponse(page([change({ id: 502, recordId: '13', changes: [{ field: 'name', old: null, new: 'Oreo' }] })]))
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Load more changes' }))

    expect(await screen.findByText('Changed · Flavours: Oreo (#13)')).toBeInTheDocument()
    expect(screen.getByText('Changed · Flavours: Chocolate (#12)')).toBeInTheDocument()
    expect(lastQuery(fetchMock, '/api/manager/audit/changes').get('cursor')).toBe('next+1')
    expect(screen.queryByRole('button', { name: 'Load more changes' })).not.toBeInTheDocument()
  })

  it('shows sign-in events with masked details, filtered by event', async () => {
    const event: SecurityEvent = {
      id: 9,
      occurredAt: '2026-11-20T06:00:00+00:00',
      eventType: 'LoginFailed',
      userId: null,
      user: 't***@example.test',
      ipAddress: '10.20.x.x',
    }
    const fetchMock = renderManagerAt('/manager/reports?tab=audit', {
      [CHANGES]: () => jsonResponse(page([])),
      [SECURITY]: () => jsonResponse(page([event])),
    })

    await screen.findByText('No changes match.')
    await userEvent.click(screen.getByRole('button', { name: 'Sign-in events' }))

    const table = await screen.findByRole('table', { name: 'Sign-in events, newest first' })
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('Sign-in failedt***@example.test10.20.x.x')

    await userEvent.selectOptions(screen.getByLabelText('Event'), 'Locked out')
    await userEvent.click(screen.getByRole('button', { name: 'Show' }))
    await waitFor(() => expect(lastQuery(fetchMock, '/api/manager/audit/security').get('eventType')).toBe('LoginLockedOut'))
  })

  it('shows why a filter was refused', async () => {
    renderManagerAt('/manager/reports?tab=audit', {
      [CHANGES]: () => jsonResponse({ status: 400, title: 'One or more validation errors occurred.', errors: { user: ['Use at most 256 characters.'] } }, 400),
    })

    expect(await screen.findByRole('alert')).toHaveTextContent('Use at most 256 characters.')
  })
})

describe('audit helpers', () => {
  it('names who made a change', () => {
    expect(describeActor({ actor: 'user', user: 'manager@milkyshaky.test' })).toBe('manager@milkyshaky.test')
    expect(describeActor({ actor: 'anonymous', user: null })).toBe('An anonymous visitor')
    expect(describeActor({ actor: 'system:paystack-webhook', user: null })).toBe('System: Paystack notification')
    expect(describeActor({ actor: 'system:something-new', user: null })).toBe('system:something-new')
    expect(describeActor({ actor: null, user: null })).toBe('Not recorded')
  })

  it('shows stored values as text, hiding masked personal data', () => {
    expect([null, '', '***', 'Vanilla', 0.15, true, { a: 1 }].map(describeValue)).toEqual(['(empty)', '(empty)', 'Hidden', 'Vanilla', '0.15', 'true', '{"a":1}'])
  })

  it('turns business-local days into UTC instants', () => {
    expect(startOfDayUtc('2026-11-20', 'Africa/Johannesburg')).toBe('2026-11-19T22:00:00.000Z')
    expect(startOfDayUtc('2026-07-01', 'Europe/London')).toBe('2026-06-30T23:00:00.000Z')
    expect(startOfDayUtc('2026-03-29', 'Europe/London')).toBe('2026-03-29T00:00:00.000Z')
    expect(nextDay('2026-12-31')).toBe('2027-01-01')
  })
})
