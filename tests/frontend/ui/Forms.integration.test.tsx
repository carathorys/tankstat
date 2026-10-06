import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it, vi } from 'vitest'
import { Field, FieldMessage } from '../../../src/frontend/forms/Field.tsx'
import { FieldDate } from '../../../src/frontend/forms/FieldDate.tsx'
import { FieldInput } from '../../../src/frontend/forms/FieldInput.tsx'
import { Form } from '../../../src/frontend/forms/Form.tsx'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'
import { dateValue, findDateField, typeDate } from '../support/dates.ts'

/** A form with a required amount (a number check and a hint), an e-mail and a date; what it sends goes to `sent`. */
function setup({ dateRequired = false, disableFuture = false } = {}) {
  const sent = vi.fn()
  function Demo() {
    const [note, setNote] = useState(false)
    return (
      <Form onSubmit={(e) => { e.preventDefault(); sent(Object.fromEntries(new FormData(e.currentTarget))) }}>
        <Field
          name="amount"
          label="Amount"
          hint="What the receipt says."
          required
          invalid={{ message: 'Enter a number.', test: (v) => v !== '' && Number.isNaN(Number(v)) }}
          extra={note && <FieldMessage>Read from the photo.</FieldMessage>}
        >
          <FieldInput />
        </Field>
        <Field name="email" label="E-mail" typeMismatch="Enter a valid e-mail address">
          <FieldInput type="email" />
        </Field>
        <Field name="day" label="Day" required={dateRequired}>
          <FieldDate disableFuture={disableFuture} />
        </Field>
        <button type="button" onClick={() => setNote(true)}>
          Read a photo
        </button>
        <button type="submit">Save</button>
      </Form>
    )
  }
  render(
    <ThemeRoot instant>
      <Demo />
    </ThemeRoot>,
  )
  return { ui: userEvent.setup(), sent }
}

it('says a missing value only after a submit, blocks it and takes the focus there', async () => {
  const { ui, sent } = setup()
  expect(screen.queryByText('Amount is required')).not.toBeInTheDocument()

  await ui.click(screen.getByRole('button', { name: 'Save' }))

  expect(sent).not.toHaveBeenCalled()
  expect(await screen.findByText('Amount is required')).toBeInTheDocument()
  expect(screen.getByLabelText('Amount')).toHaveFocus()
  expect(screen.getByLabelText('Amount')).toHaveAttribute('aria-invalid', 'true')
  expect(screen.getByLabelText('Amount')).toHaveAccessibleDescription('What the receipt says. Amount is required')
})

it('hides the message while typing and checks again when the edit is finished', async () => {
  const { ui, sent } = setup()
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  await screen.findByText('Amount is required')

  await ui.type(screen.getByLabelText('Amount'), 'abc')
  expect(screen.queryByText('Amount is required')).not.toBeInTheDocument()
  expect(screen.queryByText('Enter a number.')).not.toBeInTheDocument() // not while typing
  await ui.tab() // the edit is finished: the change event checks it

  expect(await screen.findByText('Enter a number.')).toBeInTheDocument()
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  expect(sent).not.toHaveBeenCalled() // the field's own check blocks the form too

  await ui.clear(screen.getByLabelText('Amount'))
  await ui.type(screen.getByLabelText('Amount'), '12.5')
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  expect(sent).toHaveBeenCalledWith(expect.objectContaining({ amount: '12.5', day: '' }))
})

it('checks an e-mail address, and links notes added later to the control too', async () => {
  const { ui, sent } = setup()
  await ui.type(screen.getByLabelText('Amount'), '3')
  await ui.type(screen.getByLabelText('E-mail'), 'not-an-address')
  await ui.click(screen.getByRole('button', { name: 'Save' }))

  expect(await screen.findByText('Enter a valid e-mail address')).toBeInTheDocument()
  expect(sent).not.toHaveBeenCalled()
  await ui.click(screen.getByRole('button', { name: 'Read a photo' }))
  expect(screen.getByLabelText('Amount')).toHaveAccessibleDescription('What the receipt says. Read from the photo.')
})

it('sends a typed date as YYYY-MM-DD from the hidden input', async () => {
  const { ui, sent } = setup()
  await ui.type(screen.getByLabelText('Amount'), '3')
  const day = await findDateField('Day')
  await typeDate(ui, day, '2026-03-01')

  expect(dateValue(day)).toBe('2026-03-01')
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  expect(sent).toHaveBeenCalledWith(expect.objectContaining({ day: '2026-03-01' }))
})

it('a required date that is missing is said, and the focus goes to the first part of the day', async () => {
  const { ui, sent } = setup({ dateRequired: true })
  await ui.type(screen.getByLabelText('Amount'), '3')
  const day = await findDateField('Day')

  await ui.click(screen.getByRole('button', { name: 'Save' }))

  expect(await screen.findByText('Day is required')).toBeInTheDocument()
  expect(sent).not.toHaveBeenCalled()
  expect(screen.getAllByRole('spinbutton')[0]).toHaveFocus()
  expect(day).toHaveAccessibleDescription('Day is required')
})

it('a partly typed day and a day in the future are problems the field says', async () => {
  const { ui, sent } = setup({ disableFuture: true })
  await ui.type(screen.getByLabelText('Amount'), '3')
  const day = await findDateField('Day')

  await ui.click(screen.getAllByRole('spinbutton')[0])
  await ui.keyboard('03') // the month only
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByText('Enter the whole date.')).toBeInTheDocument()

  await typeDate(ui, day, '2999-01-01')
  await waitFor(() => expect(screen.getByText('The date cannot be in the future.')).toBeInTheDocument()) // a finished day is checked at once
  await ui.click(screen.getByRole('button', { name: 'Save' }))
  expect(sent).not.toHaveBeenCalled()
})
