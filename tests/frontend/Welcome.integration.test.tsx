import { screen, within } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { server } from './server.ts'
import { fakeSummary, fakeVehicle, fakeVehicleBackend, healthHandler, person, renderWithApollo, sessionHandler, stubViewport } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

function setup(vehicles = [fakeVehicle()]) {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend(vehicles).handlers)
  renderWithApollo(<App />, '/')
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
  expect(c.getByRole('img', { name: /Spending in the last six months/ })).toBeInTheDocument()
})

it('uses the uploaded picture as the card background, and a gradient without one', async () => {
  setup([fakeVehicle({ id: 'a', name: 'With picture', pictureUrl: '/media/abc' }), fakeVehicle({ id: 'b', name: 'Plain' })])

  const withPicture = (await card('With picture')).querySelector('.vehicle-card')!
  const plain = (await card('Plain')).querySelector('.vehicle-card')!

  expect(withPicture.querySelector('img.cover')).toHaveAttribute('src', '/media/abc')
  expect(withPicture.querySelector('img.cover')).toHaveAttribute('alt', '') // decorative: the name is next to it
  expect(withPicture.querySelector('.scrim')).toBeInTheDocument() // keeps the white text readable
  expect(plain.querySelector('img')).toBeNull()
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
  expect(screen.getByRole('link', { name: 'Add vehicle' })).toHaveAttribute('href', '/vehicles')
})

it('has links to all vehicles and the import', async () => {
  setup()

  await screen.findByRole('heading', { name: 'Your vehicles' })

  expect(screen.getByRole('link', { name: 'All vehicles' })).toHaveAttribute('href', '/vehicles')
  expect(screen.getByRole('link', { name: 'Import logs' })).toHaveAttribute('href', '/import')
})
