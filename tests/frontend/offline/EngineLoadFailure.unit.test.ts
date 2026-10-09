import type { ApolloClient } from '@apollo/client'
import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { deviceData } from '../../../src/frontend/offline/deviceData.ts'
import { memoryStorage } from '../../../src/frontend/offline/deviceStorage.ts'
import { provideOfflineSync, startOfflineRuntime } from '../../../src/frontend/offline/runtime.ts'
import { usePushState } from '../../../src/frontend/offline/usePushState.ts'

// Offline before the app was kept on this device: the browser cannot fetch the engines' code (loaded on demand).
vi.mock('../../../src/frontend/offline/pull.ts', () => {
  throw new TypeError('Failed to fetch dynamically imported module: /assets/pull-abc.js')
})

afterEach(() => provideOfflineSync(null))

it('a download whose code cannot be loaded says so in the console, and the next one tries again', async () => {
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  deviceData.reset(memoryStorage())
  const client = { query: vi.fn(() => new Promise(() => undefined)), refetchQueries: vi.fn(async () => []) } as unknown as ApolloClient
  const runtime = startOfflineRuntime(client)
  try {
    await deviceData.signedIn('u1') // the download starts for them

    await waitFor(() => expect(warn).toHaveBeenCalledWith(expect.stringContaining('could not be loaded'), expect.anything()))
    await deviceData.signedIn('u2') // another one: it tries again rather than giving up for the page's life
    await waitFor(() => expect(warn).toHaveBeenCalledTimes(2))
  } finally {
    runtime.stop()
    warn.mockRestore()
  }
})

it('a screen following the sync shows nothing to follow when its code cannot be loaded, without an error', async () => {
  provideOfflineSync(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module: /assets/push-abc.js')))

  const { result } = renderHook(() => usePushState())

  await waitFor(() => expect(result.current.engine).toBeNull())
  expect(result.current.state.status).toBe('idle')
})
