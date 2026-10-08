import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it } from 'vitest'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
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
function fakeSync(park: (change: Sent) => string | null = () => null) {
  const requests: Sent[][] = []
  let failFrom: number | null = null
  server.use(
    graphql.mutation('SyncChanges', ({ variables }) => {
      const changes = variables.input.changes as Sent[]
      if (failFrom !== null && requests.length >= failFrom) return HttpResponse.error()
      requests.push(changes)
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
