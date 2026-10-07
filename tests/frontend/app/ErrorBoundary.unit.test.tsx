import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { expect, it, vi } from 'vitest'
import { ErrorBoundary } from '../../../src/frontend/ErrorBoundary.tsx'
import en from '../../../src/frontend/i18n/locales/en.json'
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
