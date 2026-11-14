import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SystemStatusCard } from './SystemStatusCard'
import type { HealthResponse } from './types'

const API_BASE_URL = 'https://api.example.test'

const healthyResponse: HealthResponse = {
  status: 'Healthy',
  checkedAtUtc: '2026-11-14T10:00:00+00:00',
  totalDurationMs: 12,
  checks: [
    { name: 'database', status: 'Healthy', description: 'Database connection succeeded.', durationMs: 10 },
    { name: 'self', status: 'Healthy', description: 'API is running.', durationMs: 1 },
  ],
}

const unhealthyResponse: HealthResponse = {
  status: 'Unhealthy',
  checkedAtUtc: '2026-11-14T10:00:00+00:00',
  totalDurationMs: 4500,
  checks: [
    { name: 'database', status: 'Unhealthy', description: 'Database is unreachable. See the API log for details.', durationMs: 4500 },
    { name: 'self', status: 'Healthy', description: 'API is running.', durationMs: 1 },
  ],
}

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: () => Promise.resolve(body) } as Response
}

describe('SystemStatusCard', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubEnv('VITE_API_BASE_URL', API_BASE_URL)
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    fetchMock.mockReset()
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it('shows every check as healthy when the API and database are up', async () => {
    fetchMock.mockResolvedValue(jsonResponse(healthyResponse))

    render(<SystemStatusCard />)

    expect(await screen.findByText('Database connection succeeded.')).toBeInTheDocument()
    expect(screen.getAllByText('Healthy')).toHaveLength(3) // overall + 2 checks
    expect(fetchMock).toHaveBeenCalledWith(`${API_BASE_URL}/api/health`, expect.anything())
  })

  it('shows the database as unhealthy when the API returns 503', async () => {
    fetchMock.mockResolvedValue(jsonResponse(unhealthyResponse, 503))

    render(<SystemStatusCard />)

    expect(await screen.findByText('Database is unreachable. See the API log for details.')).toBeInTheDocument()
    expect(screen.getAllByText('Unhealthy')).toHaveLength(2) // overall + database
  })

  it('shows a clear error when the API cannot be reached', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    render(<SystemStatusCard />)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('API not reachable')
    expect(alert).toHaveTextContent(API_BASE_URL)
  })

  it('checks again when Refresh is clicked', async () => {
    fetchMock.mockResolvedValue(jsonResponse(healthyResponse))
    const user = userEvent.setup()

    render(<SystemStatusCard />)
    await screen.findByText('Database connection succeeded.')

    await user.click(screen.getByRole('button', { name: 'Refresh' }))

    await screen.findByText('Database connection succeeded.')
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})