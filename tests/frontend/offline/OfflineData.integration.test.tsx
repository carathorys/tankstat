import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import type { PullEngine, PullState } from '../../../src/frontend/offline/pull.ts'
import { provideOfflineDownload } from '../../../src/frontend/offline/runtime.ts'
import { healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  provideOfflineDownload(null)
})
afterAll(() => server.close())

/** A download engine that does nothing but report a run. */
function fakeEngine() {
  let state: PullState = { status: 'idle', vehiclesDone: 0, vehiclesTotal: 0, lastPullAt: null, interrupted: false }
  const listeners = new Set<() => void>()
  const engine = {
    get state() {
      return state
    },
    subscribe: (listener: () => void) => {
      listeners.add(listener)
      return () => void listeners.delete(listener)
    },
    run: vi.fn(async () => {
      state = { ...state, status: 'idle', lastPullAt: Date.now() }
      listeners.forEach((l) => l())
    }),
  }
  provideOfflineDownload(engine as unknown as PullEngine)
  return engine
}

function setup(saved = { defaultWindow: 'span:P2M', vehicles: [] as { vehicleId: string; window: string }[] }) {
  stubViewport('desktop')
  const sent: unknown[] = []
  server.use(
    sessionHandler('NONE', () => null),
    healthHandler,
    graphql.query('OfflineSettings', () => HttpResponse.json({ data: { offlineSettings: { __typename: 'OfflineSettingsInfo', ...saved, vehicles: saved.vehicles.map((v) => ({ __typename: 'OfflineVehicleWindow', ...v })) } } })),
    graphql.query('OfflineVehicles', () =>
      HttpResponse.json({ data: { myVehicles: [{ __typename: 'Vehicle', id: 'v1', name: 'Octavia' }, { __typename: 'Vehicle', id: 'v2', name: 'Golf' }] } }),
    ),
    // Everything since a date: 120 and 40 entries; a window starting this year or later: 12 and 3.
    graphql.query('OfflineEstimates', ({ variables }) => {
      const all = variables.from === null
      return HttpResponse.json({
        data: {
          myVehicles: [
            { __typename: 'Vehicle', id: 'v1', logCountSince: { __typename: 'LogCountSince', refuelings: all ? 100 : 10, expenses: all ? 20 : 2 } },
            { __typename: 'Vehicle', id: 'v2', logCountSince: { __typename: 'LogCountSince', refuelings: all ? 30 : 3, expenses: all ? 10 : 0 } },
          ],
        },
      })
    }),
    graphql.mutation('UpdateOfflineSettings', ({ variables }) => {
      sent.push(variables.input)
      saved = variables.input
      return HttpResponse.json({ data: { updateOfflineSettings: { __typename: 'OfflineSettingsInfo', ...variables.input, vehicles: variables.input.vehicles.map((v: object) => ({ __typename: 'OfflineVehicleWindow', ...v })) } } })
    }),
  )
  renderWithApollo(<App />, '/account')
  return { sent, ui: userEvent.setup() }
}

const section = async () => within(await screen.findByRole('region', { name: 'Offline data' }))

it('shows the window for every vehicle and each vehicle with about how many entries it would bring', async () => {
  setup()
  const panel = await section()

  expect(await panel.findByRole('combobox', { name: 'For every vehicle' })).toHaveTextContent('A timespan back from now')
  expect(panel.getByRole('spinbutton', { name: 'Months' })).toHaveValue(2)
  const octavia = (await panel.findByRole('rowheader', { name: 'Octavia' })).closest('tr')!
  expect(within(octavia).getByText('the last 2 months (as for every vehicle)')).toBeInTheDocument()
  expect(await within(octavia).findByText('≈ 12')).toBeInTheDocument()
})

it('a vehicle can have a window of its own; Save sends the whole set and the device downloads it', async () => {
  const engine = fakeEngine()
  const { sent, ui } = setup()
  const panel = await section()
  await panel.findByRole('rowheader', { name: 'Golf' })

  await ui.click(panel.getByRole('button', { name: 'Change the window of Golf' }))
  const dialog = await screen.findByRole('dialog', { name: 'Window of Golf' })
  await ui.click(within(dialog).getByRole('combobox', { name: 'What to download' }))
  await ui.click(screen.getByRole('option', { name: 'Everything' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Apply' }))

  const golf = panel.getByRole('rowheader', { name: 'Golf' }).closest('tr')!
  expect(within(golf).getByText('everything')).toBeInTheDocument()
  expect(await within(golf).findByText('≈ 40')).toBeInTheDocument()
  expect(panel.getByText('Changes not saved yet.')).toBeInTheDocument()

  await ui.click(panel.getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(sent).toEqual([{ defaultWindow: 'span:P2M', vehicles: [{ vehicleId: 'v2', window: 'all' }] }]))
  expect(await screen.findByText('Saved. This device downloads the new window now.')).toBeInTheDocument()
  await waitFor(() => expect(engine.run).toHaveBeenCalled())
})

it('a timespan is typed in years down to seconds, and an empty one cannot be saved', async () => {
  const { sent, ui } = setup()
  const panel = await section()
  const months = await panel.findByRole('spinbutton', { name: 'Months' })

  await ui.clear(months)
  expect(panel.getByText('Enter at least one number above zero.')).toBeInTheDocument()
  expect(panel.getByRole('button', { name: 'Save' })).toBeDisabled()

  await ui.type(panel.getByRole('spinbutton', { name: 'Years' }), '2')
  await ui.type(months, '6')
  await ui.click(panel.getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(sent).toEqual([{ defaultWindow: 'span:P2Y6M', vehicles: [] }]))
})

it('offline, choosing needs the server, but the device side still works: removing what the device keeps', async () => {
  deviceData.reset(memoryStorage())
  const { ui } = setup()
  const panel = await section()
  await deviceData.settled()
  await deviceData.keep('Welcome:{}', { kept: true })
  await act(() => connectivity.failed())

  expect(await panel.findByText('Choosing what to download needs a connection to the server.')).toBeInTheDocument()
  expect(panel.queryByRole('combobox', { name: 'For every vehicle' })).not.toBeInTheDocument()

  await ui.click(panel.getByRole('button', { name: 'Remove offline data from this device' }))
  const confirm = await screen.findByRole('alertdialog', { name: 'Remove offline data from this device?' })
  await ui.click(within(confirm).getByRole('button', { name: 'Remove offline data from this device' }))

  expect(await panel.findByText('The offline data of your account is removed from this device.')).toBeInTheDocument()
  expect(await deviceData.read('Welcome:{}')).toBeUndefined()
})

it('Download now runs the download and says when it is done', async () => {
  const engine = fakeEngine()
  const { ui } = setup()
  const panel = await section()
  expect(panel.getByText('Nothing downloaded on this device yet.')).toBeInTheDocument()

  await ui.click(await panel.findByRole('button', { name: 'Download now' }))

  expect(engine.run).toHaveBeenCalled()
  expect(await panel.findByText('Downloaded.')).toBeInTheDocument()
  expect(panel.getByText(/^Last downloaded /)).toBeInTheDocument()
})
