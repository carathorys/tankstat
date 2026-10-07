import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { CHECK_EVERY_MS, RECHECK_AFTER_MS, watchForUpdates } from '../../../src/frontend/pwa/watchForUpdates.ts'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

const show = () => {
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
  document.dispatchEvent(new Event('visibilitychange'))
}

it('asks for a new version every hour, and stops when told to', () => {
  const registration = { update: vi.fn().mockResolvedValue(undefined) }
  const stop = watchForUpdates(registration)

  vi.advanceTimersByTime(CHECK_EVERY_MS)
  vi.advanceTimersByTime(CHECK_EVERY_MS)
  expect(registration.update).toHaveBeenCalledTimes(2)

  stop()
  vi.advanceTimersByTime(CHECK_EVERY_MS * 3)
  expect(registration.update).toHaveBeenCalledTimes(2)
})

it('asks when the tab is shown again after a while, not right after a check', () => {
  const registration = { update: vi.fn().mockResolvedValue(undefined) }
  const stop = watchForUpdates(registration)

  show() // only just started
  expect(registration.update).not.toHaveBeenCalled()
  vi.advanceTimersByTime(RECHECK_AFTER_MS)
  show()
  expect(registration.update).toHaveBeenCalledTimes(1)
  show() // and not twice in a row
  expect(registration.update).toHaveBeenCalledTimes(1)
  stop()
})

it('does not ask while offline, and a failed check is not an error', async () => {
  const registration = { update: vi.fn().mockRejectedValue(new Error('network')) }
  let online = false
  const stop = watchForUpdates(registration, { online: () => online })

  vi.advanceTimersByTime(CHECK_EVERY_MS)
  expect(registration.update).not.toHaveBeenCalled()

  online = true
  vi.advanceTimersByTime(CHECK_EVERY_MS)
  expect(registration.update).toHaveBeenCalledTimes(1)
  await Promise.resolve() // the rejection is swallowed: no unhandled rejection fails the run
  stop()
})
