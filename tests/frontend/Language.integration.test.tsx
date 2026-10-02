import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import App from '../../src/frontend/App.tsx'
import { i18n } from '../../src/frontend/i18n/index.ts'
import { server } from './server.ts'
import { authWarning, fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, adminSession } from './mocks.tsx'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function setup(route = '/vehicles', vehicles = [fakeVehicle()]) {
  const backend = fakeVehicleBackend(vehicles)
  server.use(adminSession([authWarning]), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, route)
  return { ...backend, ui: userEvent.setup() }
}

const switchToHungarian = async (ui: ReturnType<typeof userEvent.setup>) => {
  await ui.click(screen.getByRole('button', { name: 'Language' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'Magyar' }))
}

it('switches the whole UI to Hungarian from the language menu, and back', async () => {
  const { ui } = setup()
  await screen.findByRole('heading', { name: 'Vehicles' })

  await switchToHungarian(ui)

  await screen.findByRole('heading', { name: 'Járművek' })
  expect(screen.getByRole('button', { name: 'Jármű hozzáadása' })).toBeInTheDocument()
  expect(screen.getByRole('columnheader', { name: /Rendszám/ })).toBeInTheDocument()
  expect(screen.getByText('Dízel')).toBeInTheDocument() // enum value shown through the catalog
  expect(screen.getByText(/A hitelesítés ki van kapcsolva/)).toBeInTheDocument() // server notice code translated
  expect(document.documentElement.lang).toBe('hu')

  await ui.click(screen.getByRole('button', { name: 'Nyelv' }))
  await ui.click(await screen.findByRole('menuitemradio', { name: 'English' }))
  await screen.findByRole('heading', { name: 'Vehicles' })
  expect(document.documentElement.lang).toBe('en')
})

it('keeps the language across pages', async () => {
  const { ui } = setup()
  await screen.findByRole('heading', { name: 'Vehicles' })
  await switchToHungarian(ui)

  await ui.click(screen.getByRole('link', { name: 'Kuka' }))

  await screen.findByRole('heading', { name: 'Kuka' })
  await screen.findByText('A kuka üres.')
})

it('translates dialogs and plurals', async () => {
  const { ui } = setup('/trash')
  await screen.findByText('The trash is empty.')
  await switchToHungarian(ui)
  await screen.findByText('A kuka üres.')
  expect(i18n.t('trash.emptyDescription', { count: 2 })).toContain('2 járművet')
})

it('shows API errors in the UI language using the error key and arguments, not the English server text', async () => {
  const { ui, state } = setup('/vehicles', [])
  state.failWith = { message: 'The password must be at least 10 characters long.', key: 'password.tooShort', args: { min: 10 } }
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await ui.type(await screen.findByLabelText('Name'), 'Golf')
  await ui.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Add vehicle' }))
  expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('The password must be at least 10 characters long.')

  // Same error again after switching language: the catalog message, with the argument filled in.
  await i18n.changeLanguage('hu')
  await ui.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Jármű hozzáadása' }))
  expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('A jelszónak legalább 10 karakter hosszúnak kell lennie.')
})

it('falls back to the server message for an unknown error key', async () => {
  const { ui, state } = setup('/vehicles', [])
  state.failWith = { message: 'Something brand new went wrong.', key: 'brand.new', args: {} }
  await screen.findByText(/No vehicles yet/)

  await ui.click(screen.getByRole('button', { name: 'Add vehicle' }))
  await ui.type(await screen.findByLabelText('Name'), 'Golf')
  await ui.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Add vehicle' }))

  expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('Something brand new went wrong.')
})

it('formats the deletion time with the selected language', async () => {
  const backend = fakeVehicleBackend([], [fakeVehicle({ id: 't1', name: 'Old Fiat' })])
  server.use(adminSession(), healthHandler, ...backend.handlers)
  renderWithApollo(<App />, '/trash')
  const ui = userEvent.setup()
  await screen.findByText('Old Fiat')
  const iso = new Date('2026-10-01T08:00:00Z')
  const format = (lng: string) => new Intl.DateTimeFormat(lng, { dateStyle: 'medium', timeStyle: 'short' }).format(iso)
  expect(screen.getByText(format('en'))).toBeInTheDocument()

  await switchToHungarian(ui)

  expect(await screen.findByText(format('hu'))).toBeInTheDocument()
})
