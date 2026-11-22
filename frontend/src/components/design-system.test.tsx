import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../features/auth/AuthProvider'
import type { CurrentUser } from '../features/auth/types'
import { clearCsrfToken } from '../lib/api/httpClient'
import { jsonResponse, mockApi } from '../test/mockApi'
import { ChoiceCardGroup } from './forms/ChoiceCardGroup'
import { QuantityStepper } from './forms/QuantityStepper'
import { AppLayout } from './layout/AppLayout'
import { Alert } from './ui/Alert'
import { Badge } from './ui/Badge'
import { Button } from './ui/Button'
import { EmptyState } from './ui/EmptyState'
import { PageHeader } from './ui/PageHeader'
import { Stepper } from './ui/Stepper'

const patron: CurrentUser = {
  id: '01a1ea38-0000-7000-8000-000000000001',
  firstName: 'Thandi',
  email: 'thandi@example.test',
  mobileNumber: '+27821234567',
  roles: ['Patron'],
}

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Button', () => {
  it('never submits a form unless asked to', async () => {
    const submit = vi.fn((event: Event) => event.preventDefault())
    render(
      <form onSubmit={(event) => submit(event.nativeEvent)}>
        <Button>Plain</Button>
        <Button type="submit">Send</Button>
      </form>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Plain' }))
    expect(submit).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))
    expect(submit).toHaveBeenCalledTimes(1)
  })
})

describe('ChoiceCardGroup', () => {
  const options = [
    { value: '1', label: 'Strawberry', detail: 'R 32,00' },
    { value: '3', label: 'Vanilla', detail: 'R 30,00' },
  ]

  function Harness({ error }: { error?: string }) {
    const [value, setValue] = useState('')
    return <ChoiceCardGroup legend="Flavour" name="flavour-1" options={options} value={value} onChange={setValue} error={error} />
  }

  it('is a labelled radio group in which exactly one card is chosen', async () => {
    render(<Harness />)

    const group = screen.getByRole('group', { name: 'Flavour' })
    const strawberry = within(group).getByRole('radio', { name: /Strawberry/ })
    const vanilla = within(group).getByRole('radio', { name: /Vanilla/ })
    expect(strawberry).not.toBeChecked()

    await userEvent.click(screen.getByText('Strawberry'))
    expect(strawberry).toBeChecked()
    await userEvent.click(screen.getByText('Vanilla'))
    expect(vanilla).toBeChecked()
    expect(strawberry).not.toBeChecked()
  })

  it('reads the price as part of each choice', () => {
    render(<Harness />)

    expect(screen.getByRole('radio', { name: /^Strawberry,\s?R 32,00$/ })).toBeInTheDocument()
  })

  it('describes the group with its error', () => {
    render(<Harness error="Choose a flavour." />)

    expect(screen.getByRole('group', { name: 'Flavour' })).toHaveAccessibleDescription('Choose a flavour.')
  })
})

describe('QuantityStepper', () => {
  function Harness() {
    const [value, setValue] = useState(1)
    return (
      <QuantityStepper id="quantity" label="Number of milkshakes" value={value} min={1} max={3} onChange={setValue} unit={{ one: 'milkshake' }} />
    )
  }

  it('adds and removes within the limits', async () => {
    render(<Harness />)
    const field = screen.getByLabelText('Number of milkshakes')
    const remove = screen.getByRole('button', { name: 'Remove one milkshake' })
    const add = screen.getByRole('button', { name: 'Add one milkshake' })

    expect(field).toHaveValue(1)
    expect(remove).toBeDisabled()
    await userEvent.click(add)
    await userEvent.click(add)
    expect(field).toHaveValue(3)
    expect(add).toBeDisabled()
    await userEvent.click(remove)
    expect(field).toHaveValue(2)
  })

  it('keeps typed numbers inside the limits', async () => {
    render(<Harness />)
    const field = screen.getByLabelText('Number of milkshakes')

    await userEvent.clear(field)
    await userEvent.type(field, '9')
    expect(field).toHaveValue(3)
  })
})

describe('Stepper', () => {
  it('marks the current step and says which steps are done', () => {
    render(<Stepper label="Order progress" current={1} steps={[{ label: 'Quantity' }, { label: 'Milkshakes' }, { label: 'Pickup' }]} />)

    const nav = screen.getByRole('navigation', { name: 'Order progress' })
    expect(within(nav).getByText('Step 2 of 3: Milkshakes')).toBeInTheDocument()
    const items = within(nav).getAllByRole('listitem')
    expect(items[1]).toHaveAttribute('aria-current', 'step')
    expect(items[0]).toHaveTextContent('Quantity (done)')
    expect(items[2]).not.toHaveTextContent('(done)')
  })
})

describe('feedback components', () => {
  it('Alert announces only when given a live role, and Badge carries its meaning in words', () => {
    render(
      <>
        <Alert tone="danger" role="alert">
          Payment failed.
        </Alert>
        <Badge tone="success">Paid</Badge>
        <EmptyState title="No orders yet">Your orders appear here.</EmptyState>
        <PageHeader title="My orders" eyebrow="Account" description="Everything you have ordered." />
      </>,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Payment failed.')
    expect(screen.getByText('Paid')).toBeInTheDocument()
    expect(screen.getByText('No orders yet')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'My orders' })).toBeInTheDocument()
  })
})

describe('AppLayout', () => {
  function renderLayout(path = '/') {
    mockApi({ 'GET /api/auth/me': () => jsonResponse(patron) })
    return render(
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <Routes>
            <Route element={<AppLayout />}>
              <Route index element={<h1>Home page</h1>} />
              <Route path="orders" element={<h1>Orders page</h1>} />
            </Route>
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    )
  }

  it('has a skip link to the main content and no development notice', async () => {
    renderLayout()

    expect(await screen.findByRole('button', { name: /Log out/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute('href', '#main')
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
    expect(screen.queryByText('Development build')).not.toBeInTheDocument()
  })

  it('opens and closes the menu, closing it with Escape and after navigating', async () => {
    renderLayout()
    const menu = await screen.findByRole('button', { name: 'Menu' })
    expect(menu).toHaveAttribute('aria-expanded', 'false')
    expect(menu).toHaveAttribute('aria-controls', 'main-nav')

    await userEvent.click(menu)
    expect(menu).toHaveAttribute('aria-expanded', 'true')
    await userEvent.keyboard('{Escape}')
    expect(menu).toHaveAttribute('aria-expanded', 'false')
    expect(menu).toHaveFocus()

    await userEvent.click(menu)
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'My orders' }))
    expect(await screen.findByRole('heading', { name: 'Orders page' })).toBeInTheDocument()
    expect(menu).toHaveAttribute('aria-expanded', 'false')
  })
})
