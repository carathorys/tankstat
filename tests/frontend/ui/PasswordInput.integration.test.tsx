import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { afterEach, expect, it, vi } from 'vitest'
import { Field } from '../../../src/frontend/forms/Field.tsx'
import { Form } from '../../../src/frontend/forms/Form.tsx'
import { PasswordInput, REVEAL_IDLE_MS } from '../../../src/frontend/forms/PasswordInput.tsx'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'

afterEach(() => vi.useRealTimers())

/** A form with two passwords, each with its own Show button, and a text field to move the focus to; what it sends goes to `sent`. */
function setup({ required = false } = {}) {
  const sent = vi.fn()
  const view = render(
    <ThemeRoot instant>
      <Form
        onSubmit={(e) => {
          e.preventDefault()
          sent(Object.fromEntries(new FormData(e.currentTarget)))
        }}
      >
        <Field name="current" label="Current password" required={required}>
          <PasswordInput autoComplete="current-password" />
        </Field>
        <Field name="next" label="New password">
          <PasswordInput autoComplete="new-password" />
        </Field>
        <input aria-label="Somewhere else" />
        <button type="submit">Save</button>
      </Form>
    </ThemeRoot>,
  )
  return { ui: userEvent.setup(), sent, view }
}

const current = () => screen.getByLabelText('Current password')
const showButtons = () => screen.getAllByRole('button', { name: 'Show the password' })

it('starts hidden, keeps its autocomplete, and its own Show button shows and hides it again, the focus staying in the input', async () => {
  const { ui } = setup()
  expect(current()).toHaveAttribute('type', 'password')
  expect(current()).toHaveAttribute('autocomplete', 'current-password')
  expect(screen.getByLabelText('New password')).toHaveAttribute('autocomplete', 'new-password')
  expect(current()).toHaveAttribute('spellcheck', 'false')
  const [show, other] = showButtons()
  expect(show).toHaveAttribute('aria-pressed', 'false')
  expect(show).toHaveAttribute('aria-controls', current().id)

  await ui.click(current())
  await ui.keyboard('secret')
  await ui.click(show)

  expect(current()).toHaveAttribute('type', 'text')
  expect(current()).toHaveValue('secret')
  expect(current()).toHaveFocus() // a tap on Show is no blur
  expect(show).toHaveAttribute('aria-pressed', 'true')
  expect(other).toHaveAttribute('aria-pressed', 'false') // each field its own
  expect(screen.getByLabelText('New password')).toHaveAttribute('type', 'password')

  await ui.click(show)
  expect(current()).toHaveAttribute('type', 'password')
  expect(show).toHaveAttribute('aria-pressed', 'false')
})

it('hides again when the focus leaves the field and its button, but not on a move between the two', async () => {
  const { ui } = setup()
  await ui.click(current())
  await ui.click(showButtons()[0])

  await ui.tab() // from the input to its own Show button
  expect(showButtons()[0]).toHaveFocus()
  expect(current()).toHaveAttribute('type', 'text')

  await ui.click(screen.getByLabelText('Somewhere else'))
  expect(current()).toHaveAttribute('type', 'password')
})

it('hides again after a while without a keystroke, and each keystroke starts that wait again', async () => {
  vi.useFakeTimers()
  setup()
  act(() => current().focus())
  fireEvent.click(showButtons()[0])
  expect(current()).toHaveAttribute('type', 'text')

  act(() => vi.advanceTimersByTime(REVEAL_IDLE_MS - 1000))
  fireEvent.keyDown(current(), { key: 'a' })
  act(() => vi.advanceTimersByTime(REVEAL_IDLE_MS - 1000))
  expect(current()).toHaveAttribute('type', 'text')

  act(() => vi.advanceTimersByTime(1000))
  expect(current()).toHaveAttribute('type', 'password')
  expect(showButtons()[0]).toHaveAttribute('aria-pressed', 'false')
})

it('hides again on a submit, also one the form stops, and the value is sent as typed', async () => {
  const { ui, sent } = setup({ required: true })
  await ui.click(screen.getByLabelText('New password'))
  await ui.keyboard('new one')
  await ui.click(showButtons()[1])
  expect(screen.getByLabelText('New password')).toHaveAttribute('type', 'text')

  // The current password is required and empty: the form stops the submit, and the shown one hides all the same.
  fireEvent.submit(screen.getByRole('button', { name: 'Save' }).closest('form')!)
  expect(sent).not.toHaveBeenCalled()
  expect(screen.getByLabelText('New password')).toHaveAttribute('type', 'password')

  await ui.click(current())
  await ui.keyboard('old one')
  await ui.click(showButtons()[0])
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  expect(sent).toHaveBeenCalledWith({ current: 'old one', next: 'new one' })
  expect(current()).toHaveAttribute('type', 'password')
})

it('says while Caps Lock is on, in the field’s description, and no longer once the focus has left', async () => {
  const { ui } = setup()
  await ui.click(current())
  expect(screen.queryByText('Caps Lock is on.')).not.toBeInTheDocument()

  fireEvent.keyDown(current(), { key: 'A', modifierCapsLock: true })
  const note = await screen.findByText('Caps Lock is on.')
  expect(current()).toHaveAccessibleDescription(/Caps Lock is on\./)
  expect(note.closest('[role="status"]')).not.toBeNull()

  fireEvent.keyUp(current(), { key: 'CapsLock', modifierCapsLock: false })
  expect(screen.queryByText('Caps Lock is on.')).not.toBeInTheDocument()

  fireEvent.keyDown(current(), { key: 'A', modifierCapsLock: true })
  await screen.findByText('Caps Lock is on.')
  await ui.click(screen.getByLabelText('Somewhere else'))
  expect(screen.queryByText('Caps Lock is on.')).not.toBeInTheDocument()
})

it('is accessible shown and hidden', async () => {
  const { ui, view } = setup()
  const scan = async () =>
    expect((await axe(view.container, { rules: { 'color-contrast': { enabled: false } } })).violations.map((v) => v.id)).toEqual([])
  await scan()
  await ui.click(current())
  fireEvent.keyDown(current(), { key: 'A', modifierCapsLock: true })
  await ui.click(showButtons()[0])
  await scan()
})
