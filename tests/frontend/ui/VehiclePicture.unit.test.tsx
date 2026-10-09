import { fireEvent, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { VehiclePicture } from '../../../src/frontend/components/VehiclePicture.tsx'
import { renderWithApollo } from '../support/mocks.tsx'

it('a picture that cannot be loaded (offline, and not kept on the device) shows the placeholder, not a broken image', async () => {
  renderWithApollo(<VehiclePicture url="/media/0123456789abcdef0123456789abcdef" name="Octavia" />)

  fireEvent.error(await screen.findByRole('img', { name: 'Picture of Octavia' }))

  expect(screen.getByRole('img', { name: 'No picture' })).toBeInTheDocument()
})
