import { graphql, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it } from 'vitest'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { keptPhotos } from '../../../src/frontend/offline/keptPhotos.ts'
import { outbox, type ChangeDraft } from '../../../src/frontend/offline/outbox.ts'
import { createPushEngine } from '../../../src/frontend/offline/push.ts'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

interface Sent {
  id: string
  [operation: string]: unknown
}

/** The server's syncChanges in memory: applies everything, except the changes `park` names (with its reason), or drops the connection. */
function fakeSync(park: (change: Sent) => string | null = () => null, order?: string[]) {
  const requests: Sent[][] = []
  let failFrom: number | null = null
  server.use(
    graphql.mutation('SyncChanges', ({ variables }) => {
      const changes = variables.input.changes as Sent[]
      if (failFrom !== null && requests.length >= failFrom) return HttpResponse.error()
      requests.push(changes)
      order?.push(`sync ${changes.map((c) => c.id).join(',')}`)
      const results = changes.map((c) => {
        const key = park(c)
        return key
          ? { __typename: 'SyncChangeResultInfo', id: c.id, status: 'PARKED', entityId: null, version: null, reason: { __typename: 'SyncReasonInfo', key, args: [{ __typename: 'SyncReasonArg', name: 'expected', value: '1' }] } }
          : { __typename: 'SyncChangeResultInfo', id: c.id, status: 'APPLIED', entityId: null, version: 1, reason: null }
      })
      return HttpResponse.json({ data: { syncChanges: { __typename: 'SyncResultInfo', applied: results.filter((r) => r.status === 'APPLIED').length, parked: results.filter((r) => r.status === 'PARKED').length, results } } })
    }),
  )
  return { requests, dropFrom: (n: number) => (failFrom = n) }
}

const keep = (draft: ChangeDraft) => outbox.enqueue(draft)
const log = (id: string, vehicleId = 'v1'): ChangeDraft => ({ id, entity: 'refuelings', action: 'add', vehicleId, targetId: id, input: { id, vehicleId, date: '2026-10-01', volume: 30, totalCost: 90, currency: 'EUR', odometer: 2000, isFullTank: true } })

let pulls: number[]

beforeEach(async () => {
  deviceData.reset(memoryStorage())
  await deviceData.signedIn('u1')
  await outbox.reload()
  pulls = []
})

const engine = (chunkSize = 20) =>
  createPushEngine({ client: createApolloClient('http://localhost/graphql'), chunkSize, pull: async () => void pulls.push(outbox.changes.length) })

it('sends the changes in order, the vehicles added here first; what the server applied leaves the device after the download', async () => {
  const sync = fakeSync()
  await keep(log('r1'))
  await keep({ id: 'golf', entity: 'vehicles', action: 'add', vehicleId: 'golf', targetId: 'golf', input: { id: 'golf', name: 'Golf', fuelType: 'PETROL' } })
  await keep(log('g1', 'golf'))
  await keep({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'old', expectedVersion: 3 })

  const push = engine()
  await push.run()

  expect(sync.requests.map((r) => r.map((c) => c.id))).toEqual([['golf'], ['r1', 'g1', 't1']])
  expect(sync.requests[1][2]).toEqual({ id: 't1', expectedVersion: 3, deleteRefueling: 'old' })
  expect(pulls).toEqual([4]) // the download came while the changes were still on the device
  expect(outbox.changes).toEqual([])
  expect(push.state.last).toMatchObject({ applied: 4, parked: [], interrupted: false })
})

it('what the server parks leaves the device with its reason; what was made on a parked add waits', async () => {
  fakeSync((c) => (c.id === 'golf' || c.id === 't1' ? 'sync.versionMismatch' : null))
  await keep({ id: 'golf', entity: 'vehicles', action: 'add', vehicleId: 'golf', targetId: 'golf', input: { id: 'golf', name: 'Golf', fuelType: 'PETROL' } })
  await keep(log('g1', 'golf'))
  await keep({ id: 't1', entity: 'refuelings', action: 'trash', vehicleId: 'v1', targetId: 'old', expectedVersion: 1 })

  const push = engine()
  await push.run()

  expect(push.state.last?.parked.map((p) => [p.change.id, p.key, p.args.expected])).toEqual([['golf', 'sync.versionMismatch', '1'], ['t1', 'sync.versionMismatch', '1']])
  expect(outbox.changes.map((c) => c.id)).toEqual(['g1']) // made on the vehicle the server did not take: not sent, it waits
})

it('what was made on a parked add still waits in the next sync', async () => {
  const sync = fakeSync((c) => (c.id === 'golf' ? 'vehicle.nameRequired' : null))
  await keep({ id: 'golf', entity: 'vehicles', action: 'add', vehicleId: 'golf', targetId: 'golf', input: { id: 'golf', name: '', fuelType: 'PETROL' } })
  await keep(log('g1', 'golf'))

  await engine().run()
  await engine().run() // a new run (the add left the device in the first)

  expect(sync.requests.flat().map((c) => c.id)).toEqual(['golf'])
  expect(outbox.changes.map((c) => c.id)).toEqual(['g1'])
})

it('an edit made while a change is on its way is not taken along when the change is answered', async () => {
  let editedMeanwhile = false
  server.use(
    graphql.mutation('SyncChanges', async ({ variables }) => {
      const changes = variables.input.changes as Sent[]
      if (!editedMeanwhile) {
        editedMeanwhile = true
        await keep({ id: 'e1', entity: 'refuelings', action: 'update', vehicleId: 'v1', targetId: 'r1', input: { id: 'r1', volume: 31 } })
      }
      const results = changes.map((c) => ({ __typename: 'SyncChangeResultInfo', id: c.id, status: 'APPLIED', entityId: null, version: 1, reason: null }))
      return HttpResponse.json({ data: { syncChanges: { __typename: 'SyncResultInfo', applied: results.length, parked: 0, results } } })
    }),
  )
  await keep(log('r1'))

  await engine().run()

  expect(outbox.changes).toEqual([expect.objectContaining({ id: 'e1', action: 'update', input: { id: 'r1', volume: 31 }, expectedVersion: null })])
})

it('a request the server refuses as a whole is sent again one change at a time, and only the change it cannot take waits', async () => {
  const sent: string[][] = []
  server.use(
    graphql.mutation('SyncChanges', ({ variables }) => {
      const changes = variables.input.changes as Sent[]
      sent.push(changes.map((c) => c.id))
      if (changes.some((c) => c.id === 'bad'))
        return HttpResponse.json({ errors: [{ message: 'too large', extensions: { code: 'VALIDATION_FAILED', key: 'sync.payloadTooLarge' } }], data: null })
      const results = changes.map((c) => ({ __typename: 'SyncChangeResultInfo', id: c.id, status: 'APPLIED', entityId: null, version: 1, reason: null }))
      return HttpResponse.json({ data: { syncChanges: { __typename: 'SyncResultInfo', applied: results.length, parked: 0, results } } })
    }),
  )
  for (const id of ['a', 'bad', 'c']) await keep(log(id))

  await engine().run()

  expect(sent).toEqual([['a', 'bad', 'c'], ['a'], ['bad'], ['c']])
  expect(outbox.changes.map((c) => c.id)).toEqual(['bad'])
  expect(pulls).toHaveLength(1) // the download still ran
})

it('a lost connection ends the sync: what the server answered leaves, the rest waits for the next one', async () => {
  const sync = fakeSync()
  sync.dropFrom(1)
  for (const id of ['a', 'b', 'c']) await keep(log(id))

  const push = engine(2)
  await push.run()

  expect(outbox.changes.map((c) => c.id)).toEqual(['c'])
  expect(push.state.last).toMatchObject({ applied: 2, interrupted: true })
})

it('sends nothing while the server is out of reach, for nobody confirmed, or when nothing waits', async () => {
  const sync = fakeSync()
  await engine().run() // nothing waits
  await keep(log('a'))
  connectivity.failed()
  await engine().run()
  connectivity.reset()
  deviceData.unconfirm()
  await engine().run()

  expect(sync.requests).toEqual([])
  expect(outbox.changes).toHaveLength(1)
})

/** The draft upload of the server: each photo gets a draft id, or (`refuse`) a 4xx; records what came in which order. */
function fakeDrafts(order: string[], refuse = new Set<number>()) {
  let n = 0
  server.use(
    http.put('/media/vehicles/:vehicleId/photo-drafts', ({ request }) => {
      const nth = ++n
      order.push(`draft ${nth}${new URL(request.url).search}`)
      return refuse.has(nth) ? HttpResponse.json({ key: 'photo.tooMany' }, { status: 400 }) : HttpResponse.json({ id: `draft-${nth}`, url: `/media/draft-${nth}` })
    }),
  )
}

it('photos kept on this device go up as drafts right before their change, which sends the drafts instead; then they leave the device', async () => {
  const order: string[] = []
  fakeDrafts(order)
  const sent = fakeSync(undefined, order)
  const first = await keptPhotos.keep(new Blob([new Uint8Array([1])], { type: 'image/jpeg' }), 'v1', { purpose: 'refueling', locale: 'en' })
  const second = await keptPhotos.keep(new Blob([new Uint8Array([2])], { type: 'image/jpeg' }), 'v1')
  await keep(log('before'))
  await keep({ ...log('r1'), input: { ...log('r1').input, photoIds: [first] } })
  await keep({ id: 'p1', entity: 'refuelings', action: 'addPhoto', vehicleId: 'v1', targetId: 'saved', input: { key: second } })

  await engine().run()

  expect(order).toEqual(['sync before', 'draft 1?form=refueling&locale=en', 'sync r1', 'draft 2', 'sync p1'])
  expect(sent.requests.map((r) => r.map((c) => c.id))).toEqual([['before'], ['r1'], ['p1']]) // a change with photos goes on its own
  expect(sent.requests[1][0].logRefueling).toMatchObject({ id: 'r1', photoIds: ['draft-1'] })
  expect(sent.requests[2][0]).toMatchObject({ addRefuelingPhoto: { logId: 'saved', draftId: 'draft-2' } })
  expect(outbox.changes).toEqual([])
  expect(await keptPhotos.get(first!)).toBeUndefined()
  expect(await keptPhotos.get(second!)).toBeUndefined()
})

it('a photo the server will not take this time keeps its change waiting with the photo; one gone from the device is left out', async () => {
  const order: string[] = []
  fakeDrafts(order, new Set([1]))
  const sent = fakeSync()
  const refused = await keptPhotos.keep(new Blob([new Uint8Array([1])]), 'v1')
  await keep({ id: 'p1', entity: 'refuelings', action: 'addPhoto', vehicleId: 'v1', targetId: 'saved', input: { key: refused } })
  const kept = await keptPhotos.keep(new Blob([new Uint8Array([2])]), 'v1')
  await keep({ ...log('r1'), input: { ...log('r1').input, photoIds: [kept] } })
  await keep({ ...log('r3'), input: { ...log('r3').input, photoIds: ['local:gone'] } }) // its photo no longer on the device

  const push = engine()
  await push.run()

  expect(sent.requests.map((r) => r.map((c) => c.id))).toEqual([['r1'], ['r3']])
  expect(sent.requests[1][0].logRefueling).not.toHaveProperty('photoIds.0')
  expect(push.state.last).toMatchObject({ photosWaiting: 1, photosLeftOut: 1, interrupted: false })
  expect(outbox.changes.map((c) => c.id)).toEqual(['p1']) // not sent, so not lost
  expect(await keptPhotos.get(refused!)).toBeDefined()
  await outbox.remove(['p1'])

  server.use(http.put('/media/vehicles/:vehicleId/photo-drafts', () => HttpResponse.error()))
  const later = await keptPhotos.keep(new Blob([new Uint8Array([3])]), 'v1')
  await keep({ ...log('r2'), input: { ...log('r2').input, photoIds: [later] } })
  connectivity.reset()
  await push.run()

  expect(push.state.last).toMatchObject({ interrupted: true })
  expect(outbox.changes.map((c) => c.id)).toEqual(['r2'])
  expect(await keptPhotos.get(later!)).toBeDefined()
})
