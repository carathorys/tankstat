import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { appUpdate } from '../../../src/frontend/pwa/appUpdate.ts'
import { offlineReady } from '../../../src/frontend/pwa/offlineReady.ts'
import { registerApp, RELOAD_FALLBACK_MS, type Registration } from '../../../src/frontend/pwa/registerApp.ts'

/** A service worker as the page sees it: its state changes, and what it was told. */
class FakeWorker extends EventTarget {
  messages: unknown[] = []
  state: string
  constructor(state = 'installing') {
    super()
    this.state = state
  }
  postMessage(message: unknown) {
    this.messages.push(message)
  }
  to(state: string) {
    this.state = state
    this.dispatchEvent(new Event('statechange'))
  }
}

class FakeRegistration extends EventTarget implements Registration {
  installing: FakeWorker | null = null
  waiting: FakeWorker | null = null
  active: FakeWorker | null = null
  update = vi.fn(async () => undefined)
  /** The browser found a new version and installs it. */
  found(worker: FakeWorker) {
    this.installing = worker
    this.dispatchEvent(new Event('updatefound'))
  }
  /** It finished installing: with a worker active, it waits. */
  installed(worker: FakeWorker) {
    this.installing = null
    if (this.active) this.waiting = worker
    worker.to('installed')
  }
}

class FakeContainer extends EventTarget {
  controller: FakeWorker | null
  registration = new FakeRegistration()
  constructor(controller: FakeWorker | null) {
    super()
    this.controller = controller
  }
  register = vi.fn(async () => this.registration)
  /** A worker takes the page over. */
  takeOver(worker: FakeWorker) {
    this.controller = worker
    this.dispatchEvent(new Event('controllerchange'))
  }
}

let reload: ReturnType<typeof vi.fn<() => void>>
let onReady: ReturnType<typeof vi.fn<() => void>>

beforeEach(() => {
  reload = vi.fn<() => void>()
  onReady = vi.fn<() => void>()
})
afterEach(() => vi.useRealTimers())

/** A page opened while version 1 runs its service worker. */
async function returningVisit() {
  const current = new FakeWorker('activated')
  const container = new FakeContainer(current)
  container.registration.active = current
  await registerApp({ container, reload, onReady })
  return { container, registration: container.registration }
}

it('a first visit stores the app, says once that it is ready offline, and takes the first take-over for what it is', async () => {
  const container = new FakeContainer(null)
  const first = new FakeWorker()
  container.registration.installing = first

  await registerApp({ container, reload, onReady })
  expect(container.register).toHaveBeenCalledWith('/sw.js', { scope: '/' })
  expect(offlineReady.state).toBe('storing')

  container.registration.installed(first)
  first.to('activated')
  container.registration.active = first
  container.takeOver(first) // clientsClaim: the first worker takes the page

  expect(offlineReady.state).toBe('ready')
  expect(offlineReady.takeNews()).toBe(true)
  expect(offlineReady.takeNews()).toBe(false) // said once
  expect(onReady).toHaveBeenCalledOnce()
  expect(appUpdate.ready).toBe(false) // nothing to update
})

it('a page opened while the app is kept is ready at once, with nothing to announce', async () => {
  await returningVisit()

  expect(offlineReady.state).toBe('ready')
  expect(offlineReady.takeNews()).toBe(false)
  expect(onReady).toHaveBeenCalledOnce()
})

it('offers every new version found while the page stays open, not only the first', async () => {
  const { registration } = await returningVisit()

  for (const _ of [2, 3]) {
    const next = new FakeWorker()
    registration.found(next)
    expect(appUpdate.ready).toBe(false) // still installing
    registration.installed(next)
    expect(appUpdate.ready).toBe(true)
    expect(appUpdate.finishing).toBe(false)
    appUpdate.reset() // the person did not reload yet; the next version comes
  }
})

it('offers a version that was waiting, or still installing, when the page opened', async () => {
  const current = new FakeWorker('activated')
  const waiting = new FakeWorker('installed')
  const container = new FakeContainer(current)
  Object.assign(container.registration, { active: current, waiting })
  await registerApp({ container, reload, onReady })
  expect(appUpdate.ready).toBe(true)

  appUpdate.reset()
  const installing = new FakeWorker()
  const other = new FakeContainer(current)
  Object.assign(other.registration, { active: current, installing })
  await registerApp({ container: other, reload, onReady })
  expect(appUpdate.ready).toBe(false)
  other.registration.installed(installing)
  expect(appUpdate.ready).toBe(true)
})

it('Reload hands the page to the waiting version and reloads once it took over', async () => {
  vi.useFakeTimers()
  const { container, registration } = await returningVisit()
  const next = new FakeWorker()
  registration.found(next)
  registration.installed(next)

  appUpdate.reload()
  expect(next.messages).toEqual([{ type: 'SKIP_WAITING' }])
  expect(reload).not.toHaveBeenCalled()
  next.to('activated')
  container.takeOver(next)

  expect(reload).toHaveBeenCalledOnce()
  vi.advanceTimersByTime(RELOAD_FALLBACK_MS)
  expect(reload).toHaveBeenCalledOnce() // the fallback was called off
  expect(appUpdate.finishing).toBe(false) // its own take-over is not another tab's
})

it('Reload reloads anyway when the new version never says it took over', async () => {
  vi.useFakeTimers()
  const { registration } = await returningVisit()
  const next = new FakeWorker()
  registration.found(next)
  registration.installed(next)

  appUpdate.reload()
  vi.advanceTimersByTime(RELOAD_FALLBACK_MS - 1)
  expect(reload).not.toHaveBeenCalled()
  vi.advanceTimersByTime(1)
  expect(reload).toHaveBeenCalledOnce()
})

it('Reload just reloads when another tab already took the new version (nothing waits any more)', async () => {
  const { registration } = await returningVisit()
  const next = new FakeWorker()
  registration.found(next)
  registration.installed(next)
  registration.waiting = null // another tab's Reload: it is active now

  appUpdate.reload()

  expect(reload).toHaveBeenCalledOnce()
  expect(next.messages).toEqual([])
})

it('another tab taking the new version leaves this page behind: the notice says a reload finishes the update', async () => {
  const { container } = await returningVisit()

  container.takeOver(new FakeWorker('activated'))

  expect(appUpdate).toMatchObject({ ready: true, finishing: true, behind: true })
  appUpdate.reload()
  expect(reload).toHaveBeenCalledOnce()
})

it('a page reloaded past its worker (no controller, one active) is ready, and a later take-over is an update', async () => {
  const current = new FakeWorker('activated')
  const container = new FakeContainer(null)
  container.registration.active = current
  await registerApp({ container, reload, onReady })
  expect(offlineReady.state).toBe('ready')
  expect(offlineReady.takeNews()).toBe(false)

  container.takeOver(new FakeWorker('activated'))

  expect(appUpdate).toMatchObject({ ready: true, finishing: true })
})

it('a worker that does not register keeps nothing and offers nothing, and says why in the console', async () => {
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  const container = new FakeContainer(null)
  container.register.mockRejectedValue(new Error('insecure'))

  await registerApp({ container, reload, onReady })

  expect(offlineReady.state).toBe('unsupported')
  expect(appUpdate.ready).toBe(false)
  expect(onReady).not.toHaveBeenCalled()
  expect(warn).toHaveBeenCalledWith(expect.stringContaining('service worker did not register'), expect.any(Error))
  warn.mockRestore()
})

it('hands the registration over, so the page can ask for new versions now and then', async () => {
  const onRegistered = vi.fn<(registration: Registration) => void>()
  const container = new FakeContainer(null)

  await registerApp({ container, reload, onReady, onRegistered })

  expect(onRegistered).toHaveBeenCalledWith(container.registration)
})
