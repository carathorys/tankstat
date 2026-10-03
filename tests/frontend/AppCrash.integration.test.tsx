import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, afterEach, beforeAll, beforeEach, expect, it, vi } from 'vitest'
import App from '../../src/frontend/App.tsx'
import en from '../../src/frontend/i18n/locales/en.json'
import { server } from './server.ts'
import { fakeVehicleBackend, healthHandler, renderWithApollo, sessionHandler, stubViewport } from './mocks.tsx'

// The notifications page is the one that crashes; every other page is the real one.
vi.mock('../../src/frontend/pages/NotificationsPage.tsx', () => ({
  NotificationsPage: () => {
    throw new Error('the page broke')
  },
}))

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

let consoleError: ReturnType<typeof vi.spyOn>
beforeEach(() => {
  consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
})
afterEach(() => consoleError.mockRestore())

it('a page that crashes shows a message, leaves the shell working, and the next page starts fresh', async () => {
  stubViewport('desktop') // the navigation is docked: its links are there to click
  server.use(sessionHandler('NONE', () => null), healthHandler, ...fakeVehicleBackend([]).handlers)
  renderWithApollo(<App />, '/notifications')

  expect(await screen.findByRole('alert')).toHaveTextContent(en.app.crashed)
  expect(screen.getByRole('button', { name: en.app.reload })).toBeInTheDocument()
  expect(screen.getByRole('banner')).toHaveTextContent('Tankstat') // the shell around the page is still there
  expect(consoleError).toHaveBeenCalledWith('Rendering a page failed', expect.objectContaining({ message: 'the page broke' }), expect.any(String))

  await userEvent.click(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('link', { name: en.nav.trash }))

  await screen.findByRole('heading', { name: en.trash.title })
  expect(screen.queryByText(en.app.crashed)).not.toBeInTheDocument()
})
