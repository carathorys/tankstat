import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { adminSession, fakeLogBackend, fakeSummary, fakeVehicle, fakeVehicleBackend, healthHandler, person, renderWithApollo, sessionHandler, stubViewport } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

function setup(vehicles = [fakeVehicle()], device: 'desktop' | 'phone' = 'desktop', admin = false) {
  stubViewport(device)
  const backend = fakeVehicleBackend(vehicles)
  server.use(admin ? adminSession() : sessionHandler('NONE', () => null), healthHandler, ...backend.handlers, ...fakeLogBackend(vehicles[0] ?? fakeVehicle(), []).handlers)
  renderWithApollo(<App />, '/')
  return { ...backend, ui: userEvent.setup() }
}

const card = async (name: string) => (await screen.findByRole('link', { name: `Open ${name}` })).closest('li')!

it('shows a card per vehicle with the key figures', async () => {
  setup([fakeVehicle({ summary: fakeSummary({ latestOdometer: 123456, averageConsumption: 6.25, lastFillUpDate: '2026-09-17', thisMonthSpend: 52000 }) })])

  const c = within(await card('Octavia'))

  expect(c.getByText('ABC-123')).toBeInTheDocument()
  expect(c.getByText('Diesel')).toBeInTheDocument()
  expect(c.getByText(/123,456 km/)).toBeInTheDocument()
  expect(c.getByText('6.25 L/100 km')).toBeInTheDocument()
  expect(c.getByText('Sep 17, 2026')).toBeInTheDocument()
  expect(c.getByText(/52,000/)).toBeInTheDocument()
  expect(await c.findByRole('img', { name: /Spending in the last six months/ })).toBeInTheDocument() // the chart library loads on demand
})

it('uses the uploaded picture as the card background, and a gradient without one', async () => {
  setup([fakeVehicle({ id: 'a', name: 'With picture', pictureUrl: '/media/abc' }), fakeVehicle({ id: 'b', name: 'Plain' })])

  const withPicture = (await card('With picture')).querySelector('.vehicle-card')!
  const plain = (await card('Plain')).querySelector('.vehicle-card')!

  expect((withPicture.querySelector('.cover-picture') as HTMLElement).style.backgroundImage).toBe('url("/media/abc")') // a CSS background: cropped to fill, never stretched
  expect(withPicture.querySelector('img')).toBeNull()
  expect(withPicture.querySelector('.scrim')).toBeInTheDocument() // keeps the white text readable
  expect(plain.querySelector('.cover-picture')).toBeNull()
  expect((plain.querySelector('.cover') as HTMLElement).style.background).toContain('linear-gradient')
})

it('says what is missing for a new vehicle instead of showing empty numbers', async () => {
  setup([fakeVehicle({ summary: fakeSummary({ lastFillUpDate: null, latestOdometer: null, averageConsumption: null, currency: null, thisMonthSpend: 0, fillUpCount: 0, expenseCount: 0 }) })])

  const c = within(await card('Octavia'))

  expect(c.getByText('No fill-ups yet')).toBeInTheDocument()
  expect(c.getAllByText('No data yet').length).toBeGreaterThanOrEqual(2)
  expect(c.queryByRole('img', { name: /Spending/ })).not.toBeInTheDocument()
})

it('shows miles per gallon for vehicles in miles and gallons', async () => {
  setup([fakeVehicle({ units: { distance: 'MILES', volume: 'US_GALLONS' }, summary: fakeSummary({ averageConsumption: 8 }) })])

  expect(within(await card('Octavia')).getByText('12.5 mpg (US)')).toBeInTheDocument()
})

it('marks shared vehicles with the level of access and the owner', async () => {
  setup([fakeVehicle({ canEdit: false, logAccess: 'EDIT', owner: person('Bob') })])

  const c = within(await card('Octavia'))

  expect(c.getByText('Logs: view and edit')).toBeInTheDocument()
  expect(c.getByText('Bob')).toBeInTheDocument()
})

it('offers the next steps when there are no vehicles', async () => {
  setup([])

  await screen.findByText(/You have no vehicles yet/)

  expect(screen.getAllByRole('link', { name: 'Import logs' }).length).toBeGreaterThan(0)
  expect(screen.getByRole('button', { name: 'Add vehicle' })).toBeInTheDocument()
})

it('adds a vehicle from the home page, where everyone manages their own cars', async () => {
  const { ui, state } = setup([])
  await screen.findByText(/You have no vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add vehicle' })
  await ui.type(within(dialog).getByLabelText('Name'), 'Golf')
  await ui.click(within(dialog).getByRole('button', { name: 'Add vehicle' }))

  expect(await screen.findByRole('link', { name: 'Open Golf' })).toBeInTheDocument()
  expect(state.calls.AddVehicle).toHaveLength(1)
})

it('has a link to the import, and the full vehicle list only for administrators', async () => {
  setup()
  await screen.findByRole('heading', { name: 'Your vehicles' })

  expect(screen.getByRole('link', { name: 'Import logs' })).toHaveAttribute('href', '/import')
  expect(screen.queryByRole('link', { name: 'All vehicles' })).not.toBeInTheDocument()
})

it('shows administrators a link to all vehicles', async () => {
  setup([fakeVehicle()], 'desktop', true)

  expect(await screen.findByRole('link', { name: 'All vehicles' })).toHaveAttribute('href', '/vehicles')
})

const vehicleCard = async (name: string) => (await card(name)).querySelector('.vehicle-card') as HTMLElement

it('keeps the figures in the page for screen readers, hidden only visually until the card is hovered, focused or tapped', async () => {
  setup()

  const c = await vehicleCard('Octavia')

  expect(c.style.getPropertyValue('--panel-h')).toMatch(/px$/) // the panel's height: it sits just below the card edge until revealed (a transform, never display: none)
  expect(c).not.toHaveAttribute('data-open')
  expect(within(c).getByText('Odometer')).toBeInTheDocument()
})

it('on a touch screen the first tap shows the figures and the second tap opens the vehicle', async () => {
  const ui = userEvent.setup()
  setup([fakeVehicle()], 'phone')
  const c = await vehicleCard('Octavia')

  await ui.click(within(c).getByText('Odometer'))
  expect(c).toHaveAttribute('data-open')
  expect(within(c).getByText('Tap again to open')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Your vehicles' })).toBeInTheDocument() // the first tap did not navigate

  await ui.click(within(c).getByText('Odometer'))

  expect(await screen.findByRole('link', { name: 'Back to home' })).toBeInTheDocument() // the vehicle page
})

it('a tap on the name does the same: reveal first, open on the second tap', async () => {
  const ui = userEvent.setup()
  setup([fakeVehicle()], 'phone')
  const c = await vehicleCard('Octavia')

  await ui.click(within(c).getByRole('link', { name: 'Open Octavia' }))
  expect(c).toHaveAttribute('data-open')
  expect(screen.getByRole('heading', { name: 'Your vehicles' })).toBeInTheDocument()

  await ui.click(within(c).getByRole('link', { name: 'Open Octavia' }))
  expect(await screen.findByRole('link', { name: 'Back to home' })).toBeInTheDocument()
})

it('a tap anywhere else puts the figures away again', async () => {
  const ui = userEvent.setup()
  setup([fakeVehicle()], 'phone')
  const c = await vehicleCard('Octavia')
  await ui.click(within(c).getByText('Odometer'))
  expect(c).toHaveAttribute('data-open')

  await ui.click(screen.getByRole('heading', { name: 'Your vehicles' }))

  expect(c).not.toHaveAttribute('data-open')
})

it('the keyboard and screen readers open the vehicle straight away, even on a touch device', async () => {
  const ui = userEvent.setup()
  setup([fakeVehicle()], 'phone')
  const c = await vehicleCard('Octavia')

  within(c).getByRole('link', { name: 'Open Octavia' }).focus()
  await ui.keyboard('{Enter}')

  expect(await screen.findByRole('link', { name: 'Back to home' })).toBeInTheDocument()
})

it('with a mouse a click on the card does not toggle anything (the whole card is the link)', async () => {
  const ui = userEvent.setup()
  setup([fakeVehicle()], 'desktop')
  const c = await vehicleCard('Octavia')

  await ui.click(within(c).getByText('Odometer'))

  expect(c).not.toHaveAttribute('data-open')
})

const fleet = (n: number) =>
  Array.from({ length: n }, (_, i) =>
    fakeVehicle({ id: `v${i + 1}`, name: `Car ${String(i + 1).padStart(2, '0')}`, licensePlate: `PL-${String(i + 1).padStart(3, '0')}`, summary: fakeSummary({ fillUpCount: 0, expenseCount: 0 }) }),
  )

const cardCount = () => screen.getAllByRole('link', { name: /^Open Car/ }).length

it('loads the cards a page at a time, says how many of them are shown, and shows more on request', async () => {
  const { ui, state } = setup(fleet(30))

  await screen.findByRole('link', { name: 'Open Car 01' })
  expect(cardCount()).toBe(24)
  expect(screen.getByText('Showing 24 of 30 vehicles')).toBeInTheDocument()
  expect(screen.getByRole('list', { name: '30 vehicles' })).toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Show more vehicles' }))

  await screen.findByRole('link', { name: 'Open Car 30' })
  expect(cardCount()).toBe(30)
  expect(screen.getByText('Showing 30 of 30 vehicles')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Show more vehicles' })).not.toBeInTheDocument()
  expect(state.requests.Welcome).toEqual([
    { search: null, skip: 0, take: 24 },
    { search: null, skip: 24, take: 24 },
  ])
})

it('loads the next page by itself when the end of the list scrolls into view', async () => {
  let reveal: () => void = () => {}
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      constructor(callback: (entries: { isIntersecting: boolean }[]) => void) {
        reveal = () => callback([{ isIntersecting: true }])
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  setup(fleet(30))
  await screen.findByRole('link', { name: 'Open Car 01' })
  expect(cardCount()).toBe(24)

  reveal()

  await screen.findByRole('link', { name: 'Open Car 30' })
  expect(cardCount()).toBe(30)
})

it('searches by name on the server, and everything is back when the search is cleared', async () => {
  const { ui, state } = setup(fleet(30))
  await screen.findByRole('link', { name: 'Open Car 01' })

  await ui.type(screen.getByRole('searchbox', { name: 'Search vehicles' }), 'car 07')

  await waitFor(() => expect(cardCount()).toBe(1))
  expect(screen.getByRole('link', { name: 'Open Car 07' })).toBeInTheDocument()
  expect(screen.getByText('Showing 1 of 1 vehicles')).toBeInTheDocument()
  expect(state.requests.Welcome.at(-1)).toEqual({ search: 'car 07', skip: 0, take: 24 })
  expect(state.requests.Welcome.some((r) => r.search === 'c')).toBe(false) // not a request for every letter

  await ui.clear(screen.getByRole('searchbox', { name: 'Search vehicles' }))
  await waitFor(() => expect(cardCount()).toBe(24))
})

it('also finds a vehicle by its license plate', async () => {
  const { ui } = setup(fleet(30))
  await screen.findByRole('link', { name: 'Open Car 01' })

  await ui.type(screen.getByRole('searchbox', { name: 'Search vehicles' }), 'PL-012')

  await waitFor(() => expect(cardCount()).toBe(1))
  expect(screen.getByRole('link', { name: 'Open Car 12' })).toBeInTheDocument()
})

it('says when nothing matches the search, and keeps the search box to change it', async () => {
  const { ui } = setup()
  await screen.findByRole('link', { name: 'Open Octavia' })

  await ui.type(screen.getByRole('searchbox', { name: 'Search vehicles' }), 'zzz')

  expect(await screen.findByText('No vehicles match "zzz".')).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: 'Open Octavia' })).not.toBeInTheDocument()
  expect(screen.getByRole('searchbox', { name: 'Search vehicles' })).toBeInTheDocument()
})

it('has no search box before the first vehicle exists', async () => {
  setup([])

  await screen.findByText(/You have no vehicles yet/)

  expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
})
