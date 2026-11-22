import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import type { EmailDelivery, EmailDeliveryList } from './types'

const LIST = 'GET /api/manager/emails'
const REF = 'MS-7KQ2-X9TD'

function email(overrides: Partial<EmailDelivery> = {}): EmailDelivery {
  return {
    id: 41,
    kind: 'OrderConfirmed',
    status: 'Sent',
    orderReference: REF,
    recipientEmail: 'thandi@example.test',
    attemptCount: 1,
    lastError: null,
    createdAt: '2026-11-20T08:00:00+00:00',
    lastAttemptAt: '2026-11-20T08:00:05+00:00',
    nextAttemptAt: null,
    sentAt: '2026-11-20T08:00:05+00:00',
    provider: 'Mailpit',
    resendCount: 0,
    canResend: true,
    ...overrides,
  }
}

function list(items: EmailDelivery[], counts: Partial<EmailDeliveryList['counts']> = {}): EmailDeliveryList {
  return { items, counts: { Pending: 0, Sending: 0, Sent: 0, Failed: 0, Suppressed: 0, ...counts } }
}

const failed = email({ id: 42, kind: 'RefundFailed', status: 'Failed', attemptCount: 8, lastError: 'ProviderUnavailable', sentAt: null, provider: null })
const waiting = email({
  id: 43,
  kind: 'CancellationRequested',
  status: 'Pending',
  attemptCount: 2,
  lastError: 'Timeout',
  nextAttemptAt: '2026-11-20T08:04:00+00:00',
  sentAt: null,
  provider: null,
  canResend: false,
})
const suppressed = email({ id: 44, kind: 'OrderCancelled', status: 'Suppressed', attemptCount: 1, sentAt: null, provider: null, canResend: false })

beforeEach(() => clearCsrfToken())
afterEach(() => vi.unstubAllGlobals())

function query(fetchMock: ReturnType<typeof renderManagerAt>, index: number): URLSearchParams {
  const call = callsTo(fetchMock, 'GET', '/api/manager/emails')[index]
  return new URL(String(call[0]), 'http://localhost').searchParams
}

describe('Management → Emails', () => {
  it('is in the management navigation', async () => {
    renderManagerAt('/manager', {})

    const nav = await screen.findByRole('navigation', { name: 'Management sections' })
    expect(within(nav).getByRole('link', { name: 'Emails' })).toHaveAttribute('href', '/manager/emails')
  })

  it('lists emails with their delivery state and counts per status', async () => {
    renderManagerAt('/manager/emails', {
      [LIST]: () => jsonResponse(list([email(), failed, waiting, suppressed], { Sent: 5, Failed: 1, Pending: 1, Suppressed: 1 })),
    })

    const sent = await screen.findByRole('listitem', { name: `Order confirmation and receipt for order ${REF}` })
    expect(sent).toHaveTextContent('Sent')
    expect(sent).toHaveTextContent('To thandi@example.test')
    expect(sent).toHaveTextContent('via Mailpit')

    const gaveUp = screen.getByRole('listitem', { name: `Refund not completed for order ${REF}` })
    expect(gaveUp).toHaveTextContent('Failed')
    expect(gaveUp).toHaveTextContent('the email service was unavailable')
    expect(gaveUp).toHaveTextContent('8 attempts')

    const retrying = screen.getByRole('listitem', { name: `Cancellation request for order ${REF}` })
    expect(retrying).toHaveTextContent('Waiting to send')
    expect(retrying).toHaveTextContent('next try')
    expect(within(retrying).queryByRole('button')).not.toBeInTheDocument()

    expect(screen.getByRole('listitem', { name: `Order cancelled for order ${REF}` })).toHaveTextContent('not on the allow-list')

    const filters = screen.getByRole('group', { name: 'Show emails by status' })
    expect(within(filters).getByRole('button', { name: 'All (8)' })).toHaveAttribute('aria-pressed', 'true')
    expect(within(filters).getByRole('button', { name: 'Sent (5)' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('filters by status and by order reference', async () => {
    const fetchMock = renderManagerAt('/manager/emails', { [LIST]: () => jsonResponse(list([email()], { Sent: 1 })) })
    await screen.findByRole('listitem', { name: `Order confirmation and receipt for order ${REF}` })

    await userEvent.click(screen.getByRole('button', { name: 'Failed (0)' }))
    await waitFor(() => expect(callsTo(fetchMock, 'GET', '/api/manager/emails')).toHaveLength(2))
    expect(query(fetchMock, 1).get('status')).toBe('Failed')
    expect(screen.getByRole('button', { name: 'Failed (0)' })).toHaveAttribute('aria-pressed', 'true')

    await userEvent.type(screen.getByLabelText('Order reference'), ` ${REF.toLowerCase()} `)
    await userEvent.click(screen.getByRole('button', { name: 'Show' }))
    await waitFor(() => expect(callsTo(fetchMock, 'GET', '/api/manager/emails')).toHaveLength(3))
    expect(query(fetchMock, 2).get('order')).toBe(REF.toLowerCase())
    expect(screen.getByRole('heading', { name: `Emails for order ${REF}` })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'All orders' }))
    await waitFor(() => expect(callsTo(fetchMock, 'GET', '/api/manager/emails')).toHaveLength(4))
    expect(query(fetchMock, 3).get('order')).toBeNull()
  })

  it('sends a failed email again and refreshes the list', async () => {
    let resent = false
    const fetchMock = renderManagerAt('/manager/emails', {
      [LIST]: () => jsonResponse(list([resent ? { ...failed, status: 'Pending', attemptCount: 0, resendCount: 1, canResend: false } : failed], { Failed: 1 })),
      'POST /api/manager/emails/42/resend': () => {
        resent = true
        return jsonResponse({ ...failed, status: 'Pending', canResend: false })
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: `Send again: the refund not completed email for order ${REF}` }))

    expect(await screen.findByRole('status')).toHaveTextContent(`The refund not completed email for order ${REF} will be sent again shortly.`)
    const item = await screen.findByRole('listitem', { name: `Refund not completed for order ${REF}` })
    await waitFor(() => expect(item).toHaveTextContent('Waiting to send'))
    expect(item).toHaveTextContent('sent again once')
    expect(callsTo(fetchMock, 'POST', '/api/manager/emails/42/resend')).toHaveLength(1)
    const [, init] = callsTo(fetchMock, 'POST', '/api/manager/emails/42/resend')[0] as [unknown, RequestInit]
    expect(new Headers(init.headers).get('X-XSRF-TOKEN')).toBe('test-token')
  })

  it('shows why a resend was refused', async () => {
    renderManagerAt('/manager/emails', {
      [LIST]: () => jsonResponse(list([email()], { Sent: 1 })),
      'POST /api/manager/emails/41/resend': () =>
        jsonResponse({ status: 409, title: 'Only a sent or failed email can be sent again.' }, 409),
    })

    await userEvent.click(await screen.findByRole('button', { name: `Send again: the order confirmation and receipt email for order ${REF}` }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Only a sent or failed email can be sent again.')
  })

  it('asks the manager to wait when resends are rate-limited', async () => {
    renderManagerAt('/manager/emails', {
      [LIST]: () => jsonResponse(list([email()], { Sent: 1 })),
      'POST /api/manager/emails/41/resend': () => jsonResponse({ status: 429, title: 'Too Many Requests' }, 429),
    })

    await userEvent.click(await screen.findByRole('button', { name: `Send again: the order confirmation and receipt email for order ${REF}` }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Please wait a minute and try again.')
  })

  it('says when no emails match', async () => {
    renderManagerAt('/manager/emails', { [LIST]: () => jsonResponse(list([])) })
    expect(await screen.findByText('No emails match.')).toBeInTheDocument()
  })

  it('reports a list that cannot be loaded', async () => {
    renderManagerAt('/manager/emails', { [LIST]: () => jsonResponse({ status: 500, title: 'The server could not load the emails.' }, 500) })
    expect(await screen.findByRole('alert')).toHaveTextContent('The server could not load the emails.')
  })
})
