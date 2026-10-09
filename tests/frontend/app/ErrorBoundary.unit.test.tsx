import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { lazy, type ReactNode } from 'react'
import { expect, it, vi } from 'vitest'
import { ErrorBoundary } from '../../../src/frontend/ErrorBoundary.tsx'
import en from '../../../src/frontend/i18n/locales/en.json'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'
import { silenceConsoleError } from '../support/mocks.tsx'

function Boom(): ReactNode {
  throw new Error('boom')
}

it('shows what is inside while nothing fails', () => {
  render(
    <ThemeRoot instant>
      <ErrorBoundary>
        <p>Fine</p>
      </ErrorBoundary>
    </ThemeRoot>,
  )

  expect(screen.getByText('Fine')).toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('a page that fails to render is replaced by a message and a reload button, and the cause goes to the console', async () => {
  const consoleError = silenceConsoleError() // React prints a failed render too
  const onReload = vi.fn()
  render(
    <ThemeRoot instant>
      <ErrorBoundary onReload={onReload}>
        <Boom />
      </ErrorBoundary>
    </ThemeRoot>,
  )

  expect(await screen.findByRole('alert')).toHaveTextContent(en.app.crashed)
  await userEvent.click(screen.getByRole('button', { name: en.app.reload }))
  expect(onReload).toHaveBeenCalledOnce()
  expect(consoleError).toHaveBeenCalledWith('Rendering a page failed', expect.objectContaining({ message: 'boom' }), expect.any(String))
})

/** A lazy page whose code the browser could not fetch (a newer release took its place, or it is not on this device yet). */
const MissingPage = lazy(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module: /assets/TrashPage-4Y4PGAq8.js')))

function renderMissingPage(onReload: () => void) {
  return render(
    <ThemeRoot instant>
      <ErrorBoundary onReload={onReload}>
        <MissingPage />
      </ErrorBoundary>
    </ThemeRoot>,
  )
}

it('a page whose code cannot be loaded is reloaded once for the current version, without an error on screen', async () => {
  silenceConsoleError()
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  const onReload = vi.fn()

  renderMissingPage(onReload)

  await waitFor(() => expect(onReload).toHaveBeenCalledOnce())
  expect(screen.getByRole('status')).toHaveTextContent(en.app.loading)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  warn.mockRestore()
})

it('when that reload did not help, the page shows the ordinary error with its reload button', async () => {
  silenceConsoleError()
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  sessionStorage.setItem('tankstat.chunkReload', JSON.stringify({ url: window.location.pathname + window.location.search, at: Date.now() }))
  const onReload = vi.fn()

  renderMissingPage(onReload)

  expect(await screen.findByRole('alert')).toHaveTextContent(en.app.crashed)
  expect(onReload).not.toHaveBeenCalled()
  warn.mockRestore()
})

it('offline, a page not kept on this device yet says so calmly instead of crashing', async () => {
  silenceConsoleError()
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
  act(() => connectivity.failed())
  const onReload = vi.fn()

  renderMissingPage(onReload)

  expect(await screen.findByRole('status')).toHaveTextContent(en.app.notOfflineYet)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  expect(onReload).not.toHaveBeenCalled()
  warn.mockRestore()
})
