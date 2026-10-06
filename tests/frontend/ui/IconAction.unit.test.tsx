import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Pencil } from 'lucide-react'
import { expect, it, vi } from 'vitest'
import { IconAction } from '../../../src/frontend/components/IconAction.tsx'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'

it('is named by its label, which is also the tooltip shown on hover and focus', async () => {
  const ui = userEvent.setup()
  const click = vi.fn()
  render(
    <ThemeRoot instant>
      <IconAction label="Edit Octavia" onClick={click}>
        <Pencil size={16} aria-hidden />
      </IconAction>
    </ThemeRoot>,
  )
  const button = screen.getByRole('button', { name: 'Edit Octavia' })

  await ui.hover(button)
  expect(await screen.findByRole('tooltip')).toHaveTextContent('Edit Octavia')
  await ui.click(button)
  expect(click).toHaveBeenCalledOnce()
})

it('a disabled one keeps its name and tooltip, and does nothing', async () => {
  const ui = userEvent.setup()
  const click = vi.fn()
  render(
    <ThemeRoot instant>
      <IconAction label="Refresh" disabled onClick={click}>
        <Pencil size={16} aria-hidden />
      </IconAction>
    </ThemeRoot>,
  )
  const button = screen.getByRole('button', { name: 'Refresh' })
  expect(button).toBeDisabled()

  await ui.hover(button.parentElement!)
  expect(await screen.findByRole('tooltip')).toHaveTextContent('Refresh')
  expect(click).not.toHaveBeenCalled()
})
