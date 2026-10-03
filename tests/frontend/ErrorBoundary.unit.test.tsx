import { Theme } from '@radix-ui/themes'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ErrorBoundary } from '../../src/frontend/ErrorBoundary.tsx'
import en from '../../src/frontend/i18n/locales/en.json'

let consoleError: ReturnType<typeof vi.spyOn>
beforeEach(() => {
  consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined) // React prints a failed render too
})
afterEach(() => consoleError.mockRestore())

function Boom(): ReactNode {
  throw new Error('boom')
}

it('shows what is inside while nothing fails', () => {
  render(
    <Theme>
      <ErrorBoundary>
        <p>Fine</p>
      </ErrorBoundary>
    </Theme>,
  )

  expect(screen.getByText('Fine')).toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('a page that fails to render is replaced by a message and a reload button, and the cause goes to the console', async () => {
  const onReload = vi.fn()
  render(
    <Theme>
      <ErrorBoundary onReload={onReload}>
        <Boom />
      </ErrorBoundary>
    </Theme>,
  )

  expect(await screen.findByRole('alert')).toHaveTextContent(en.app.crashed)
  await userEvent.click(screen.getByRole('button', { name: en.app.reload }))
  expect(onReload).toHaveBeenCalledOnce()
  expect(consoleError).toHaveBeenCalledWith('Rendering a page failed', expect.objectContaining({ message: 'boom' }), expect.any(String))
})

it('a new key starts the next page fresh', () => {
  const view = render(
    <Theme>
      <ErrorBoundary key="/broken">
        <Boom />
      </ErrorBoundary>
    </Theme>,
  )
  expect(screen.getByRole('alert')).toBeInTheDocument()

  view.rerender(
    <Theme>
      <ErrorBoundary key="/next">
        <p>Next page</p>
      </ErrorBoundary>
    </Theme>,
  )

  expect(screen.getByText('Next page')).toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})
