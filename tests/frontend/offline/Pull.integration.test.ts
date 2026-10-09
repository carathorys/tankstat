import type { ApolloClient } from '@apollo/client'
import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { answerLocally } from '../../../src/frontend/offline/localResolvers.ts'
import { createPullEngine, PAGE_SIZE } from '../../../src/frontend/offline/pull.ts'
import { snapshotKey } from '../../../src/frontend/offline/snapshotPolicy.ts'
import { fakeVehicle, fakeVehicleBackend, gqlError, person, silenceConsoleError } from '../support/mocks.tsx'
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

it('by default it pages by the server maximum and tells its listeners how far it got; a run asked for meanwhile follows the one under way', async () => {
  feed.setRule('all') // the window does not depend on the device's real clock
  feed.add('a', '2026-09-01')
  const pull = createPullEngine({ client: createApolloClient('http://localhost/graphql') })
  const seen: string[] = []
  const stop = pull.subscribe(() => seen.push(`${pull.state.status} ${pull.state.vehiclesDone}/${pull.state.vehiclesTotal}`))
  let during: Promise<void> | null = null
  feed.onAsk((n) => {
    if (n === 1) during = pull.run()
  })

  const first = pull.run()
  await first

  expect(during).toBe(first) // the same promise: it covers the run that followed
  expect(feed.asked).toEqual([{ vehicleId: 'v1', from: null, take: PAGE_SIZE }, { vehicleId: 'v1', since: expect.any(String), take: PAGE_SIZE }])
  expect(seen).toEqual(['pulling 0/0', 'pulling 0/1', 'pulling 1/1', 'idle 1/1', 'pulling 0/0', 'pulling 0/1', 'pulling 1/1', 'idle 1/1'])
  expect(pull.state.lastPullAt).toEqual(expect.any(Number))

  stop()
  await pull.run()
  expect(seen).toHaveLength(8) // no longer told
})

it('a window of nothing removes the logs the device held, and asks the server for none', async () => {
  feed.add('a', '2026-09-01')
  const pull = engine(50)
  await pull.run()
  expect(await localIds()).toEqual(['a'])

  feed.setRule('none')
  feed.asked.length = 0
  await pull.run()

  expect(await localIds()).toEqual([])
  expect(feed.asked).toEqual([])
  expect(await (await deviceData.rows())!.cursor('v1')).toMatchObject({ from: false, complete: true })

  await pull.run() // still nothing: nothing to remove or ask
  expect(feed.asked).toEqual([])
  expect(await (await deviceData.rows())!.cursor('v1')).toMatchObject({ from: false, complete: true })
})

it('a vehicle of its own rule follows it; one without, the default, and with no default the last two months', async () => {
  vehicles.state.vehicles.push({ ...fakeVehicle({ id: 'v2', name: 'Superb' }), deletedAt: '' })
  feed.add('old', '2026-03-01')
  feed.add('old2', '2026-03-01', 'v2')
  server.use(
    graphql.query('OfflineSettings', () =>
      HttpResponse.json({ data: { offlineSettings: { __typename: 'OfflineSettingsInfo', defaultWindow: null, vehicles: [{ __typename: 'OfflineVehicleSettingsInfo', vehicleId: 'v1', window: 'all' }] } } }),
    ),
  )

  await engine(50).run()

  expect(await localIds('v1')).toEqual(['old'])
  expect(await localIds('v2')).toEqual([])
  expect(feed.asked).toEqual(expect.arrayContaining([{ vehicleId: 'v1', from: null, take: 50 }, { vehicleId: 'v2', from: '2026-08-07', take: 50 }]))
})

it('a later download that changed more than a page goes on page by page, and applies what was removed once', async () => {
  for (const id of ['a', 'b', 'c', 'd']) feed.add(id, '2026-09-01')
  const pull = engine(2)
  await pull.run()
  feed.later()
  feed.asked.length = 0

  for (const id of ['a', 'b', 'c']) feed.edit(id, { volume: 50 })
  feed.purge('d')
  await pull.run()

  expect(feed.asked).toEqual([{ vehicleId: 'v1', since: expect.any(String), take: 2 }, { vehicleId: 'v1', after: expect.any(String), take: 2 }])
  expect(await localIds()).toEqual(['a', 'b', 'c'])
  const rows = (await deviceData.rows())!
  expect((await rows.logs('refuelings', 'v1')).map((r) => r.volume)).toEqual([50, 50, 50])
})

const refused = (key?: string) =>
  gqlError('Refused.', key ? 'NOT_FOUND' : 'INTERNAL', key)

it('a vehicle the user lost access to since the home list was asked is dropped from the device', async () => {
  feed.add('a', '2026-09-01')
  const pull = engine(50)
  await pull.run()

  server.use(graphql.query('OfflineChanges', () => HttpResponse.json(refused('vehicle.notFound'))))
  await pull.run()

  expect(await localIds()).toEqual([])
  expect(await (await deviceData.rows())!.cursor('v1')).toBeUndefined()
  expect(pull.state).toMatchObject({ status: 'idle', interrupted: false, vehiclesDone: 1 })
})

it('a vehicle whose download fails for another reason keeps what it had, and the next run tries again', async () => {
  silenceConsoleError()
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  onTestFinished(() => warn.mockRestore())
  feed.add('a', '2026-09-01')
  const pull = engine(50)
  await pull.run()

  server.use(graphql.query('OfflineChanges', () => HttpResponse.json(refused())))
  await pull.run()

  expect(warn).toHaveBeenCalledWith(expect.stringContaining('download of a vehicle failed'), expect.anything())
  expect(await localIds()).toEqual(['a'])
  expect(pull.state).toMatchObject({ status: 'idle', interrupted: false, vehiclesDone: 1 })

  server.use(...feed.handlers) // the server answers again
  feed.add('b', '2026-09-02')
  await pull.run()
  expect(await localIds()).toEqual(['a', 'b'])
})

it('a download that fails before the vehicles is warned about, not taken for a lost connection', async () => {
  silenceConsoleError()
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  onTestFinished(() => warn.mockRestore())
  server.use(graphql.query('OfflineSettings', () => HttpResponse.json(refused())))
  const pull = engine()

  await pull.run()

  expect(warn).toHaveBeenCalledWith(expect.stringContaining('The offline download failed'), expect.anything())
  expect(pull.state).toMatchObject({ status: 'idle', interrupted: false, lastPullAt: null })
  expect(feed.asked).toEqual([])
})

it('a home list answered without data is a failed download', async () => {
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  onTestFinished(() => warn.mockRestore())
  const client = { query: vi.fn(async () => ({ data: undefined })) } as unknown as ApolloClient

  const pull = createPullEngine({ client, now })
  await pull.run()

  expect(warn).toHaveBeenCalledWith(expect.stringContaining('The offline download failed'), new Error('The server sent no data.'))
  expect(pull.state).toMatchObject({ status: 'idle', interrupted: false, lastPullAt: null })
})

it('with more vehicles than the download lists, one missing from the list stays on the device', async () => {
  vehicles.state.vehicles.push({ ...fakeVehicle({ id: 'v2', name: 'Superb' }), deletedAt: '' })
  feed.add('a', '2026-09-01', 'v2')
  const pull = engine(50)
  await pull.run()
  expect(await localIds('v2')).toEqual(['a'])

  // The list stops before v2 (as it would past its 200 vehicles): not seeing it says nothing about it.
  server.use(graphql.query('Welcome', () => HttpResponse.json({ data: { myVehicles: [{ ...vehicles.state.vehicles[0], recurring: [] }], myVehicleCount: 2, vehicleTotal: 2 } })))
  await pull.run()

  expect(await localIds('v2')).toEqual(['a'])
  expect((await (await deviceData.rows())!.vehicles()).map((v) => v.id).sort()).toEqual(['v1', 'v2'])
})

describe('pictures', () => {
  const PICTURE = '1'.repeat(32)
  const OWNER = '2'.repeat(32)
  const ME = '3'.repeat(32)
  let asked: string[]
  /** The server's pictures: each answers with its id as bytes, or the status given. */
  const media = (answer: (id: string) => Response | undefined = () => undefined) =>
    http.get('*/media/:id', ({ params }) => {
      const id = String(params.id)
      asked.push(id)
      return answer(id) ?? new HttpResponse(new TextEncoder().encode(id), { headers: { 'Content-Type': 'image/webp' } })
    })
  const kept = async () => (await (await deviceData.rows())!.pictureIds()).sort()

  beforeEach(async () => {
    asked = []
    vehicles = fakeVehicleBackend([
      fakeVehicle({ id: 'v1', name: 'Octavia', pictureUrl: `/media/${PICTURE}`, owner: person('Alice', { avatarUrl: `/media/${OWNER}` }) }),
    ])
    server.use(...vehicles.handlers)
    await deviceData.keep(snapshotKey('Session', undefined), { session: { mode: 'STANDALONE', user: { id: 'u1', avatarUrl: `/media/${ME}` } } })
  })

  it('keeps the downloaded vehicles\' pictures, their owners\' avatars and the user\'s own, and never asks for one twice', async () => {
    server.use(media())
    const pull = engine()

    await pull.run()

    expect(await kept()).toEqual([PICTURE, OWNER, ME].sort())
    const picture = await (await deviceData.rows())!.picture(PICTURE)
    expect(picture?.type).toBe('image/webp')
    expect(new TextDecoder().decode(picture!.bytes)).toBe(PICTURE)

    await pull.run()
    expect(asked.sort()).toEqual([PICTURE, OWNER, ME].sort()) // an image never changes under its id
  })

  it('a picture no longer shown goes, and one the server does not give is left out until the next download', async () => {
    const NEW = '4'.repeat(32)
    server.use(media((id) => (id === NEW ? new HttpResponse(null, { status: 404 }) : undefined)))
    const pull = engine()
    await pull.run()

    vehicles.state.vehicles[0]!.pictureUrl = `/media/${NEW}` // changed on another device
    await pull.run()

    expect(await kept()).toEqual([OWNER, ME].sort())
    expect(pull.state).toMatchObject({ status: 'idle', interrupted: false })
  })

  it('a picture asked for after the access cookie ran out is asked again after a refresh', async () => {
    let refreshed = false
    server.use(
      http.post('/auth/token/refresh', () => {
        refreshed = true
        return new HttpResponse(null, { status: 204 })
      }),
      media(() => (refreshed ? undefined : new HttpResponse(null, { status: 401 }))),
    )

    await engine().run()

    expect(refreshed).toBe(true)
    expect(await kept()).toEqual([PICTURE, OWNER, ME].sort())
  })

  it('a full storage ends the pictures, never the download', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    const rows = (await deviceData.rows())!
    vi.spyOn(rows, 'putPicture').mockRejectedValue(Object.assign(new Error('full'), { name: 'QuotaExceededError' }))
    feed.add('a', '2026-09-01')
    server.use(media())
    const pull = engine()

    await pull.run()

    expect(await localIds()).toEqual(['a'])
    expect(pull.state).toMatchObject({ status: 'idle', interrupted: false, lastPullAt: expect.any(Number) })
    expect(asked.length).toBeLessThan(3) // stops after the pictures already on their way (two at a time), never asks for the third
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('no room'))
    warn.mockRestore()
  })

  it('a lost connection ends the pictures quietly, after the logs are kept', async () => {
    feed.add('a', '2026-09-01')
    server.use(http.get('*/media/:id', () => HttpResponse.error()))
    const pull = engine()

    await pull.run()

    expect(await localIds()).toEqual(['a'])
    expect(await kept()).toEqual([])
    expect(pull.state).toMatchObject({ status: 'idle', lastPullAt: expect.any(Number) })
    expect(connectivity.reachable).toBe(false)
  })

  it('keeps no picture once the server names another account', async () => {
    server.use(
      media(() => {
        void deviceData.signedIn('u2') // someone else signed in while the picture was on its way
        return undefined
      }),
    )

    await engine().run()

    expect(asked.length).toBeGreaterThan(0)
    expect(await kept()).toEqual([])
    await deviceData.signedIn('u1')
    expect(await kept()).toEqual([])
  })
})
