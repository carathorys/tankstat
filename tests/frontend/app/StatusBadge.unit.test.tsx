import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { StatusBadge } from '../../../src/frontend/StatusBadge.tsx'

it('shows Healthy for ok', () => {
  render(<StatusBadge status="ok" />)
  expect(screen.getByRole('status')).toHaveTextContent('Healthy')
})

it('shows Degraded for anything else', () => {
  render(<StatusBadge status="down" />)
  expect(screen.getByRole('status')).toHaveTextContent('Degraded')
})
