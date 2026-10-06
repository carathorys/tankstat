import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { expect, it } from 'vitest'
import { CurrencyInput } from '../../../src/frontend/forms/CurrencyInput.tsx'
import { Field } from '../../../src/frontend/forms/Field.tsx'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'

function Currency() {
  const [value, setValue] = useState('')
  return (
    <ThemeRoot instant>
      <Field name="currency" label="Currency" hint="Three letters">
        <CurrencyInput value={value} onChange={setValue} preferred="EUR" />
      </Field>
      <output>{value}</output>
    </ThemeRoot>
  )
}

it('suggests currencies by name, with the code as the value, in a list named by the field', async () => {
  const ui = userEvent.setup()
  render(<Currency />)
  const input = screen.getByLabelText('Currency')
  expect(input).toHaveAccessibleDescription('Three letters')

  await ui.type(input, 'forint')
  expect(screen.getByRole('listbox', { name: 'Currency' })).toBeInTheDocument()
  await ui.click(screen.getByRole('option', { name: 'HUF – Hungarian Forint' }))

  expect(input).toHaveValue('HUF')
})

it('takes any three letters typed (free text), shown in capitals', async () => {
  const ui = userEvent.setup()
  render(<Currency />)

  await ui.type(screen.getByLabelText('Currency'), 'xau')

  expect(screen.getByLabelText('Currency')).toHaveValue('xau') // sent in capitals by the dialog
  expect(screen.getByLabelText('Currency')).toHaveStyle({ textTransform: 'uppercase' })
})
