import { afterAll, afterEach, beforeAll, beforeEach, expect, it } from 'vitest'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { answerLocally } from '../../../src/frontend/offline/localResolvers.ts'
import { createPullEngine } from '../../../src/frontend/offline/pull.ts'
import { snapshotKey } from '../../../src/frontend/offline/snapshotPolicy.ts'
import { fakeVehicle, fakeVehicleBackend } from '../support/mocks.tsx'
import { fakeFeed, now } from '../support/offlineFeed.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

let feed: ReturnType<typeof fakeFeed>
let vehicles: ReturnType<typeof fakeVehicleBackend>

beforeEach(async () => {
  deviceData.reset(memoryStorage())
  await deviceData.signedIn('u1')
  feed = fakeFeed()
  vehicles = fakeVehicleBackend([fakeVehicle({ id: 'v1', name: 'Octavia' })])
  server.use(...vehicles.handlers, ...feed.handlers)
})

const engine = (pageSize = 2) => createPullEngine({ client: createApolloClient('http://localhost/graphql'), now, pageSize })

async function localIds(vehicleId = 'v1') {
  const rows = await deviceData.rows()
  return ((await rows!.logs('refuelings', vehicleId)).map((r) => r.id)).sort()
}

it('a first download keeps the logs of the window page by page, and the answers of the vehicle page and the home page', async () => {
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-15'], ['c', '2026-10-01'], ['d', '2026-08-10'], ['e', '2026-10-05']]) feed.add(id, date)
  feed.add('old', '2026-05-01') // before the window

  await engine().run()

  expect(await localIds()).toEqual(['a', 'b', 'c', 'd', 'e'])
  expect(feed.asked).toHaveLength(3) // five rows in pages of two
  expect(feed.asked[0]).toMatchObject({ vehicleId: 'v1', from: '2026-08-07' })
  expect((await deviceData.read(snapshotKey('VehicleDetails', { id: 'v1' })))?.data).toMatchObject({ vehicle: { id: 'v1' } })
  expect((await deviceData.read(snapshotKey('RecurringExpenses', { vehicleId: 'v1' })))?.data).toMatchObject({ vehicle: { recurring: [{ id: 's-v1' }] } })
  expect((await deviceData.read(snapshotKey('Welcome', { search: null, skip: 0, take: 24 })))?.data).toMatchObject({ myVehicles: [{ id: 'v1' }], vehicleTotal: 1 })
  const page = await answerLocally(await deviceData.rows(), 'Refuelings', { vehicleId: 'v1', orderBy: 'DATE', direction: 'ASC', skip: 2, take: 2 })
  expect((page!.refuelings as { id: string }[]).map((r) => r.id)).toEqual(['b', 'c'])
})

it('a later download brings only what changed since, whatever its date, and what was removed for good', async () => {
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-15'], ['c', '2026-10-01']]) feed.add(id, date)
  const pull = engine(50)
  await pull.run()
  feed.later()
  feed.asked.length = 0

  feed.edit('a', { volume: 42 })
  feed.edit('b', { date: '2026-06-01' }) // moved out of the window: it must go from the device
  feed.purge('c')
  feed.add('d', '2026-10-06')
  await pull.run()

  expect(feed.asked).toEqual([{ vehicleId: 'v1', since: expect.any(String), take: 50 }]) // one request, no window: what changed
  expect(await localIds()).toEqual(['a', 'd'])
  expect((await (await deviceData.rows())!.log('refuelings', 'a'))?.volume).toBe(42)
})

it('a wider window downloads in full again; a narrower one drops the older logs without asking for anything', async () => {
  feed.add('recent', '2026-10-01')
  feed.add('spring', '2026-04-01')
  const pull = engine(50)
  await pull.run()
  expect(await localIds()).toEqual(['recent'])

  feed.later()
  feed.setRule('thisYear')
  await pull.run()
  expect(await localIds()).toEqual(['recent', 'spring'])
  expect(feed.asked.at(-1)).toMatchObject({ from: '2026-01-01' }) // in full, from the new start

  feed.later()
  feed.setRule('span:P1M')
  feed.asked.length = 0
  await pull.run()
  expect(await localIds()).toEqual(['recent'])
  expect(feed.asked).toEqual([{ vehicleId: 'v1', since: expect.any(String), take: 50 }]) // only what changed
})

it('a download too old for what the server remembers starts afresh, and what it did not bring again is gone', async () => {
  feed.add('a', '2026-09-01')
  feed.add('b', '2026-09-02')
  const pull = engine(50)
  await pull.run()
  feed.later()

  feed.resync()
  feed.purge('b') // its tombstone is long gone on the server
  await pull.run()

  expect(await localIds()).toEqual(['a'])
  expect(feed.asked.at(-1)).toMatchObject({ from: '2026-08-07' })
})

it('a vehicle no longer on the home list goes, with everything below it', async () => {
  feed.add('a', '2026-09-01')
  const pull = engine(50)
  await pull.run()

  vehicles.state.vehicles.splice(0, 1) // trashed, purged or no longer shared
  await pull.run()

  expect(await localIds()).toEqual([])
  expect(await (await deviceData.rows())!.cursor('v1')).toBeUndefined()
})

it('a lost connection ends the run, and the next one goes on where the full download stopped', async () => {
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-02'], ['c', '2026-09-03'], ['d', '2026-09-04'], ['e', '2026-09-05']]) feed.add(id, date)
  const pull = engine(2)
  feed.failAfter(1) // the second page does not come
  await pull.run()
  expect(pull.state.interrupted).toBe(true)
  expect(await localIds()).toEqual(['a', 'b'])
  expect(await answerLocally(await deviceData.rows(), 'Refuelings', { vehicleId: 'v1' })).toBeUndefined() // not complete: not answered

  connectivity.reset()
  feed.failAfter(null)
  await pull.run()

  expect(feed.asked[2]).toMatchObject({ after: expect.any(String) }) // went on, did not start again
  expect(await localIds()).toEqual(['a', 'b', 'c', 'd', 'e'])
})

it('a window going from nothing to something answers nothing offline until its first download is through', async () => {
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-02'], ['c', '2026-09-03']]) feed.add(id, date)
  feed.setRule('none')
  const pull = engine(2)
  await pull.run()

  feed.later()
  feed.setRule('all')
  feed.failAfter(1) // the second page does not come
  await pull.run()

  expect(await localIds()).toEqual(['a', 'b'])
  expect(await answerLocally(await deviceData.rows(), 'Refuelings', { vehicleId: 'v1' })).toBeUndefined() // a first page is not the list
})

it('a download the server can no longer continue starts afresh the next time', async () => {
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-02'], ['c', '2026-09-03']]) feed.add(id, date)
  const pull = engine(2)
  feed.failAfter(1)
  await pull.run()
  connectivity.reset()
  feed.failAfter(null)
  feed.refuseCursors() // a new server version reads its cursors differently
  const before = feed.asked.length

  await pull.run()
  feed.acceptCursors()
  await pull.run()

  expect(feed.asked[before]).toMatchObject({ after: expect.any(String) }) // where it stopped: refused
  expect(feed.asked[before + 1]).not.toHaveProperty('after') // the next run starts from the window again
  expect(await localIds()).toEqual(['a', 'b', 'c'])
})

it('a run stops keeping anything once the server names another account', async () => {
  for (const [id, date] of [['a', '2026-09-01'], ['b', '2026-09-02'], ['c', '2026-09-03']]) feed.add(id, date)
  const pull = engine(2)
  feed.onAsk(async (n) => {
    if (n === 1) await deviceData.signedIn('u2') // someone else signed in while the first page was on its way
  })

  await pull.run()

  await deviceData.signedIn('u1')
  expect(await localIds()).toEqual([])
})

it('downloads nothing for someone the server did not confirm', async () => {
  deviceData.reset(memoryStorage())
  feed.add('a', '2026-09-01')

  await engine().run()

  expect(feed.asked).toEqual([])
})
