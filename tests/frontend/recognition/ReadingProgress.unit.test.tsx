import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { ReadingProgress } from '../../../src/frontend/recognition/ReadingProgress.tsx'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'

it('says a photo is being read and that saving now is fine, with a bar of the wait that has passed', () => {
  const now = Date.now()
  render(
    <ThemeRoot instant>
      <ReadingProgress active canSave since={now - 55_000} until={now + 55_000} />
    </ThemeRoot>,
  )

  expect(screen.getByText(/Reading the photo/)).toHaveTextContent(/save/i)
  const bar = screen.getByTestId('reading-wait')
  expect(bar).toHaveAttribute('aria-hidden', 'true') // the words are what a screen reader hears
  expect(Number(bar.getAttribute('aria-valuenow'))).toBeGreaterThanOrEqual(49)
  expect(Number(bar.getAttribute('aria-valuenow'))).toBeLessThanOrEqual(52)
})

it('shows nothing once no photo is being read', () => {
  const { container } = render(
    <ThemeRoot instant>
      <ReadingProgress active={false} canSave={false} />
    </ThemeRoot>,
  )
  expect(container).toBeEmptyDOMElement()
})
