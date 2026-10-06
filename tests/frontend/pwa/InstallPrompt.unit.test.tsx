import { act, renderHook } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { platform } from '../../../src/frontend/pwa/platform.ts'
import { useInstallPrompt } from '../../../src/frontend/pwa/useInstallPrompt.ts'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

/** What Chromium fires when it would show its install banner. */
const offer = (outcome: 'accepted' | 'dismissed' = 'accepted') => {
  const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
    prompt: vi.fn().mockResolvedValue(undefined),
    userChoice: Promise.resolve({ outcome, platform: 'web' }),
  })
  act(() => {
    window.dispatchEvent(event)
  })
  return event
}

it("keeps the browser's offer instead of its banner, and shows it on request, once", async () => {
  const { result } = renderHook(() => useInstallPrompt())
  expect(result.current.canInstall).toBe(false)

  const event = offer()

  expect(event.defaultPrevented).toBe(true)
  expect(result.current.canInstall).toBe(true)
  let accepted = false
  await act(async () => {
    accepted = await result.current.install()
  })
  expect(accepted).toBe(true)
  expect(event.prompt).toHaveBeenCalledTimes(1)
  expect(result.current.canInstall).toBe(false) // spent
})

it('forgets the offer once the app is installed', () => {
  const { result } = renderHook(() => useInstallPrompt())
  offer()
  expect(result.current.canInstall).toBe(true)

  act(() => {
    window.dispatchEvent(new Event('appinstalled'))
  })

  expect(result.current.canInstall).toBe(false)
})

it('offers nothing while running as an installed app', () => {
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('display-mode: standalone'), media: query, addEventListener() {}, removeEventListener() {} }))
  vi.spyOn(platform, 'isIos').mockReturnValue(true)
  const { result } = renderHook(() => useInstallPrompt())
  expect(result.current.showIosHint).toBe(false) // installed already: no how-to either

  offer()

  expect(result.current.canInstall).toBe(false)
  expect(result.current.showIosHint).toBe(false)
})

it('a dismissed dialog is no installation, and the offer is spent', async () => {
  const { result } = renderHook(() => useInstallPrompt())
  offer('dismissed')

  let accepted = true
  await act(async () => {
    accepted = await result.current.install()
  })

  expect(accepted).toBe(false)
  expect(result.current.canInstall).toBe(false)
})

it('a browser that refuses to show the dialog is reported, not thrown', async () => {
  const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
  const { result } = renderHook(() => useInstallPrompt())
  const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
    prompt: vi.fn().mockRejectedValue(new DOMException('not now', 'NotAllowedError')),
    userChoice: new Promise<never>(() => undefined),
  })
  act(() => {
    window.dispatchEvent(event)
  })

  let accepted = true
  await act(async () => {
    accepted = await result.current.install()
  })

  expect(accepted).toBe(false)
  expect(consoleError).toHaveBeenCalledWith('The install prompt could not be shown', expect.any(DOMException))
})

it('asks an iPhone to use the Share menu, unless the browser offers to install itself', () => {
  vi.spyOn(platform, 'isIos').mockReturnValue(true)
  const { result } = renderHook(() => useInstallPrompt())
  expect(result.current.showIosHint).toBe(true)

  offer()

  expect(result.current.showIosHint).toBe(false)
  expect(result.current.canInstall).toBe(true)
})
