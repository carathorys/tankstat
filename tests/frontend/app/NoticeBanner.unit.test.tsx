import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { NoticeBanner } from '../../../src/frontend/NoticeBanner.tsx'
import en from '../../../src/frontend/i18n/locales/en.json'
import { authWarning } from '../support/mocks.tsx'

it('renders nothing without notices', () => {
  const { container } = render(<NoticeBanner notices={[]} />)
  expect(container).toBeEmptyDOMElement()
})

it('shows a warning notice prominently', () => {
  render(<NoticeBanner notices={[authWarning]} />)
  const note = screen.getByRole('note')
  expect(note).toHaveAttribute('data-severity', 'WARNING')
  expect(note).toHaveTextContent('Warning:')
  expect(note).toHaveTextContent(en.notices.AUTH_DISABLED)
})

it('shows several notices, info ones without the warning label', () => {
  render(<NoticeBanner notices={[authWarning, { code: 'X', severity: 'INFO', message: 'FYI' }]} />)
  const notes = screen.getAllByRole('note')
  expect(notes).toHaveLength(2)
  expect(notes[1]).not.toHaveTextContent('Warning:')
})
