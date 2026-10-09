import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, onTestFinished, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { LAST_PULL_KEY, type PullEngine, type PullState } from '../../../src/frontend/offline/pull.ts'
import { provideOfflineDownload } from '../../../src/frontend/offline/runtime.ts'
import { dateValue, findDateField } from '../support/dates.ts'
import { gqlError, healthHandler, renderWithApollo, sessionHandler, silenceConsoleError, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  provideOfflineDownload(null)
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

/** A download engine that does nothing but report a run. */
function fakeEngine() {
  let state: PullState = { status: 'idle', vehiclesDone: 0, vehiclesTotal: 0, lastPullAt: null, interrupted: false }
  const listeners = new Set<() => void>()
  const set = (next: Partial<PullState>) => {
    state = { ...state, ...next }
    listeners.forEach((l) => l())
  }
  const engine = {
    get state() {
      return state
    },
    subscribe: (listener: () => void) => {
      listeners.add(listener)
      return () => void listeners.delete(listener)
    },
    run: vi.fn(async () => set({ status: 'idle', lastPullAt: Date.now() })),
    set,
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
  const view = renderWithApollo(<App />, '/account')
  return { sent, view, ui: userEvent.setup() }
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

it('the window of a vehicle no longer listed (unshared, trashed) is not sent back with a save', async () => {
  const { sent, ui } = setup({ defaultWindow: 'span:P2M', vehicles: [{ vehicleId: 'v1', window: 'all' }, { vehicleId: 'gone', window: 'none' }] })
  const panel = await section()
  await panel.findByRole('rowheader', { name: 'Golf' })
  expect(panel.queryByText('Changes not saved yet.')).not.toBeInTheDocument()

  await ui.type(panel.getByRole('spinbutton', { name: 'Months' }), '1') // 2 → 21
  await ui.click(panel.getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(sent).toEqual([{ defaultWindow: 'span:P21M', vehicles: [{ vehicleId: 'v1', window: 'all' }] }]))
})

it('removing what the device keeps waits until a running download is over', async () => {
  const engine = fakeEngine()
  let finish = () => undefined as void
  engine.run.mockImplementation(
    () =>
      new Promise<void>((resolve) => {
        engine.set({ status: 'pulling', vehiclesTotal: 2 })
        finish = () => {
          engine.set({ status: 'idle', lastPullAt: Date.now() })
          resolve()
        }
      }),
  )
  const { ui } = setup()
  const panel = await section()
  const remove = panel.getByRole('button', { name: 'Remove offline data from this device' })

  await ui.click(await panel.findByRole('button', { name: 'Download now' }))
  expect(remove).toBeDisabled()

  await act(async () => finish())
  expect(remove).toBeEnabled()
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

it('a save the server refuses says why, and the choice stays to be saved', async () => {
  silenceConsoleError()
  const { ui } = setup()
  server.use(graphql.mutation('UpdateOfflineSettings', () => HttpResponse.json(gqlError('Gone', 'NOT_FOUND', 'vehicle.notFound'))))
  const panel = await section()

  await ui.type(await panel.findByRole('spinbutton', { name: 'Years' }), '1')
  await ui.click(panel.getByRole('button', { name: 'Save' }))

  expect(await panel.findByRole('alert')).toHaveTextContent('This vehicle does not exist.')
  expect(panel.getByText('Changes not saved yet.')).toBeInTheDocument()
})

it('a vehicle downloading nothing brings no entries, and one with a window of its own can follow the default again', async () => {
  const { sent, ui } = setup({ defaultWindow: 'span:P2M', vehicles: [{ vehicleId: 'v1', window: 'all' }, { vehicleId: 'v2', window: 'none' }] })
  const panel = await section()
  const golf = (await panel.findByRole('rowheader', { name: 'Golf' })).closest('tr')!
  expect(within(golf).getByText('nothing')).toBeInTheDocument()
  expect(within(golf).getByText('≈ 0')).toBeInTheDocument() // nothing to ask the server about

  await ui.click(panel.getByRole('button', { name: 'Change the window of Octavia' }))
  const dialog = await screen.findByRole('dialog', { name: 'Window of Octavia' })
  await ui.click(within(dialog).getByRole('combobox', { name: 'What to download' }))
  await ui.click(screen.getByRole('option', { name: 'The same as for every vehicle' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Apply' }))

  const octavia = panel.getByRole('rowheader', { name: 'Octavia' }).closest('tr')!
  expect(within(octavia).getByText('the last 2 months (as for every vehicle)')).toBeInTheDocument()
  await ui.click(panel.getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(sent).toEqual([{ defaultWindow: 'span:P2M', vehicles: [{ vehicleId: 'v2', window: 'none' }] }]))
})

it('cancelling a vehicle’s window dialog changes nothing', async () => {
  const { ui } = setup()
  const panel = await section()
  await panel.findByRole('rowheader', { name: 'Golf' })

  await ui.click(panel.getByRole('button', { name: 'Change the window of Golf' }))
  const dialog = await screen.findByRole('dialog', { name: 'Window of Golf' })
  await ui.click(within(dialog).getByRole('combobox', { name: 'What to download' }))
  await ui.click(screen.getByRole('option', { name: 'Everything' }))
  await ui.click(within(dialog).getByRole('button', { name: 'Cancel' }))

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  const golf = panel.getByRole('rowheader', { name: 'Golf' }).closest('tr')!
  expect(within(golf).getByText('the last 2 months (as for every vehicle)')).toBeInTheDocument()
  expect(panel.queryByText('Changes not saved yet.')).not.toBeInTheDocument()
})

it('an account without a window starts from the last two months, ready to be saved', async () => {
  setup({ defaultWindow: '', vehicles: [] })
  const panel = await section()

  expect(await panel.findByRole('spinbutton', { name: 'Months' })).toHaveValue(2)
  expect(panel.getByText('Changes not saved yet.')).toBeInTheDocument()
})

it('a window this version cannot read starts as an empty timespan to fill in', async () => {
  setup({ defaultWindow: 'span:P0D', vehicles: [] })
  const panel = await section()

  expect(await panel.findByRole('combobox', { name: 'For every vehicle' })).toHaveTextContent('A timespan back from now')
  expect(panel.getByRole('spinbutton', { name: 'Months' })).toHaveValue(null)
  expect(panel.getByText('Enter at least one number above zero.')).toBeInTheDocument()
  expect(panel.getByRole('button', { name: 'Save' })).toBeDisabled()
})

it('a timespan of more than 100 years cannot be saved', async () => {
  const { ui } = setup()
  const panel = await section()

  await ui.type(await panel.findByRole('spinbutton', { name: 'Years' }), '101')

  expect(panel.getByText('At most 100 years back.')).toBeInTheDocument()
  expect(panel.getByRole('group', { name: 'How far back' })).toHaveAccessibleDescription('At most 100 years back.')
  expect(panel.getByRole('button', { name: 'Save' })).toBeDisabled()
})

it('a window since a date shows the date, which can be changed; without a date it cannot be saved', async () => {
  const { sent, ui } = setup({ defaultWindow: 'from:2026-01-15', vehicles: [] })
  const panel = await section()
  expect(await panel.findByRole('combobox', { name: 'For every vehicle' })).toHaveTextContent('Since a date')
  const since = () => findDateField(/^Since/, panel.getByRole('combobox', { name: 'For every vehicle' }).closest('section')!)
  expect(dateValue(await since())).toBe('2026-01-15')

  await ui.click(within(await since()).getAllByRole('spinbutton')[0])
  await ui.keyboard('{Control>}a{/Control}{Backspace}')
  await waitFor(() => expect(panel.getByRole('button', { name: 'Save' })).toBeDisabled())

  await ui.keyboard('03012026') // the field still has the focus: month, day, year
  expect(dateValue(await since())).toBe('2026-03-01')
  await ui.click(panel.getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(sent).toEqual([{ defaultWindow: 'from:2026-03-01', vehicles: [] }]))
})

it('says how much this site stores on the device, when the browser tells', async () => {
  Object.defineProperty(navigator, 'storage', { configurable: true, value: { estimate: () => Promise.resolve({ usage: 12_345_678 }) } })
  onTestFinished(() => void Reflect.deleteProperty(navigator, 'storage'))
  setup()
  const panel = await section()

  expect(await panel.findByText('This site uses about 12.3 MB on this device.')).toBeInTheDocument()
})

it.each([
  ['refuses to estimate', { estimate: () => Promise.reject(new Error('denied')) }],
  ['has no estimate', {}],
  ['estimates without the usage', { estimate: () => Promise.resolve({ quota: 1_000_000_000 }) }],
])('a browser that %s shows nothing about the storage used', async (_, storage) => {
  Object.defineProperty(navigator, 'storage', { configurable: true, value: storage })
  onTestFinished(() => void Reflect.deleteProperty(navigator, 'storage'))
  setup()
  const panel = await section()

  await panel.findByText('Nothing downloaded on this device yet.')
  await deviceData.settled()
  expect(panel.queryByText(/This site uses about/)).not.toBeInTheDocument()
})

it('says when the device last downloaded, also after a reload', async () => {
  deviceData.reset(memoryStorage())
  const { view } = setup()
  await section()
  await deviceData.settled()
  const finished = Date.UTC(2026, 9, 5, 7, 30)
  await deviceData.keep(LAST_PULL_KEY, finished)
  view.unmount()

  renderWithApollo(<App />, '/account')
  const panel = await section()

  const time = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(finished))
  expect(await panel.findByText(`Last downloaded ${time}.`)).toBeInTheDocument()
})

it('after removing what the device keeps it says nothing is downloaded, until the next download', async () => {
  deviceData.reset(memoryStorage())
  const engine = fakeEngine()
  engine.run.mockImplementation(async () => engine.set({ status: 'idle', lastPullAt: Date.now() + 1 }))
  const { ui } = setup()
  const panel = await section()
  await ui.click(await panel.findByRole('button', { name: 'Download now' }))
  expect(await panel.findByText(/^Last downloaded /)).toBeInTheDocument()

  await ui.click(panel.getByRole('button', { name: 'Remove offline data from this device' }))
  const confirm = await screen.findByRole('alertdialog', { name: 'Remove offline data from this device?' })
  await ui.click(within(confirm).getByRole('button', { name: 'Remove offline data from this device' }))
  expect(await panel.findByText('Nothing downloaded on this device yet.')).toBeInTheDocument()

  await ui.click(panel.getByRole('button', { name: 'Download now' }))
  expect(await panel.findByText(/^Last downloaded /)).toBeInTheDocument()
})

it('a download cut short by the server going out of reach says it goes on later, not that it is done', async () => {
  const engine = fakeEngine()
  engine.run.mockImplementation(async () => engine.set({ status: 'idle', interrupted: true }))
  const { ui } = setup()
  const panel = await section()

  await ui.click(await panel.findByRole('button', { name: 'Download now' }))

  expect(await panel.findByText('The download stopped because the server cannot be reached; it goes on later.')).toBeInTheDocument()
  expect(panel.queryByText('Downloaded.')).not.toBeInTheDocument()
})
