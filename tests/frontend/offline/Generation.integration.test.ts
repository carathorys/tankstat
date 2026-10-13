import { graphql, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import { createApolloClient } from '../../../src/frontend/apolloClient.ts'
import { SessionDocument } from '../../../src/frontend/gql/generated.ts'
import { deviceData, GENERATION_KEY } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { startOfflineRuntime } from '../../../src/frontend/offline/runtime.ts'
import { fakeInstance } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const session = (generation: string) => ({ session: { __typename: 'Session', mode: 'NONE', user: null, instance: fakeInstance(generation) }, notices: [] })

it('a Session answer with another generation sets the downloaded rows aside, drops them and the kept answers, keeps the outbox, and downloads afresh; the first generation seen wipes nothing', async () => {
  deviceData.reset(memoryStorage())
  const client = createApolloClient('http://localhost/graphql')
  let answer = session('g1')
  let downloads = 0
  server.use(
    graphql.query('Session', () => HttpResponse.json({ data: answer })),
    graphql.query('Welcome', () => (downloads++, new Promise<never>(() => undefined))), // the download starts; it need not finish here
  )
  const runtime = startOfflineRuntime(client)
  try {
    await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })
    await deviceData.settled()
    const rows = (await deviceData.writableRows())!
    await rows.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 1 }])
    await rows.putLogs('refuelings', [{ id: 'r1', vehicleId: 'v1', date: '2026-09-01', version: 1, pulledAt: 1, volume: 40 }])
    await rows.putChanges([{ id: 'c1', seq: 1, createdAt: 1, entity: 'vehicles', action: 'update', vehicleId: 'v1', targetId: 'v1', input: { name: 'Mine' } }])
    await deviceData.keep('Welcome:{}', { kept: true })
    expect((await deviceData.read(GENERATION_KEY))?.data).toBe('g1') // adopted, nothing dropped
    expect(await rows.vehicles()).toHaveLength(1)
    const downloadsBefore = downloads

    answer = session('g2') // the server was restored meanwhile
    await client.query({ query: SessionDocument, fetchPolicy: 'network-only' })
    await deviceData.settled()
    await vi.waitFor(async () => expect((await deviceData.read(GENERATION_KEY))?.data).toBe('g2'))

    expect(await rows.vehicles()).toEqual([])
    expect(await rows.logs('refuelings', 'v1')).toEqual([])
    expect(await deviceData.read('Welcome:{}')).toBeUndefined()
    expect((await deviceData.read('Session:{}'))?.data).toEqual(answer) // the answer that said so is kept after the wipe
    expect(await rows.changes()).toHaveLength(1)
    const found = await rows.found()
    expect(found.map((r) => [r.entity, r.id, r.generation]).sort()).toEqual([['refuelings', 'r1', 'g1'], ['vehicles', 'v1', 'g1']])
    expect(found.find((r) => r.id === 'r1')?.values).toEqual({ date: '2026-09-01', volume: 40 })
    await vi.waitFor(() => expect(downloads).toBeGreaterThan(downloadsBefore))
  } finally {
    runtime.stop()
  }
})
