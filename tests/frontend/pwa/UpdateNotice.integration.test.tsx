import { act, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { afterAll, afterEach, beforeAll, expect, it, vi } from 'vitest'
import App from '../../../src/frontend/App.tsx'
import { appUpdate } from '../../../src/frontend/pwa/appUpdate.ts'
import { fakeVehicle, fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from '../support/mocks.tsx'
import { server } from '../support/server.ts'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

function setup() {
  stubViewport('desktop')
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend([fakeVehicle()]).handlers)
  renderWithApollo(<App />, '/')
  return userEvent.setup()
}

it('says nothing until the browser has a new version, then offers it and reloads only when asked', async () => {
  const ui = setup()
  await screen.findByText('Octavia')
  expect(screen.queryByText('A new version of Tankstat is available.')).not.toBeInTheDocument()

  const reload = vi.fn()
  act(() => appUpdate.offer(reload))

  expect(await screen.findByText('A new version of Tankstat is available.')).toBeInTheDocument()
  expect(reload).not.toHaveBeenCalled() // nothing reloads by itself
  await ui.click(screen.getByRole('button', { name: 'Reload' }))
  expect(reload).toHaveBeenCalledTimes(1)
})

it('is a status without accessibility violations', async () => {
  setup()
  await screen.findByText('Octavia')
  act(() => appUpdate.offer(() => undefined))

  const message = (await screen.findByText('A new version of Tankstat is available.')).closest('[role="status"]') as HTMLElement
  expect((await axe(message, { rules: { 'color-contrast': { enabled: false } } })).violations).toEqual([])
})

it('after another tab took the new version, says a reload finishes the update', async () => {
  const ui = setup()
  await screen.findByText('Octavia')
  const reload = vi.fn()

  act(() => appUpdate.offer(reload, { finishing: true }))

  expect(await screen.findByText('Tankstat was updated in another tab. Reload to finish updating.')).toBeInTheDocument()
  expect(screen.queryByText('A new version of Tankstat is available.')).not.toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Reload' }))
  expect(reload).toHaveBeenCalledTimes(1)
})
