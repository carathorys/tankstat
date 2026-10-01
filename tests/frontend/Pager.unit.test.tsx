import { Theme } from '@radix-ui/themes'
import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Pager } from '../../src/frontend/grid/Pager.tsx'
import { i18n } from '../../src/frontend/i18n/index.ts'

const pager = (total: number, page = 0) =>
  render(
    <Theme>
      <Pager page={page} pageSize={10} total={total} onPage={() => {}} onPageSize={() => {}} />
    </Theme>,
  )

it('formats the range and numbers for the current language', async () => {
  pager(1234)
  expect(screen.getByText('1–10 of 1,234')).toBeInTheDocument()
})

it('uses the Hungarian number format and wording after switching language', async () => {
  await i18n.changeLanguage('hu')
  pager(12345)
  expect(screen.getByText(/^1–10 \/ 12\s345$/)).toBeInTheDocument() // Hungarian groups with a (non-breaking) space, from five digits up
  expect(screen.getByRole('button', { name: 'Következő oldal' })).toBeEnabled()
})

it('shows 0–0 for an empty list and disables both arrows', () => {
  pager(0)
  expect(screen.getByText('0–0 of 0')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
})

it('disables next on the last page', () => {
  pager(25, 2)
  expect(screen.getByText('21–25 of 25')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled()
})
