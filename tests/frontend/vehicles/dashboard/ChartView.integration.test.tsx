import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { ChartView } from '../../../../src/frontend/dashboard/ChartView.tsx'
import type { ChartData, ChartRecipe } from '../../../../src/frontend/dashboard/chartFormat.ts'
import { ThemeRoot } from '../../../../src/frontend/theme/ThemeRoot.tsx'

const units = { distance: 'KILOMETERS', volume: 'LITERS' } as const
const money: ChartData = {
  unit: 'CURRENCY',
  series: [
    { kind: 'fuel', currency: 'HUF', points: [{ key: '2026-08', value: 12000 }, { key: '2026-09', value: 15500 }] },
    { kind: 'expenses', currency: 'HUF', points: [{ key: '2026-08', value: 3000 }, { key: '2026-09', value: null }] },
  ],
}
const recipe = (kind: ChartRecipe['kind']): Pick<ChartRecipe, 'kind' | 'metric' | 'grouping' | 'stacked'> => ({ kind, metric: 'TOTAL_SPEND', grouping: 'MONTH', stacked: kind === 'BAR' })

const view = (data: ChartData, kind: ChartRecipe['kind']) =>
  render(
    <ThemeRoot instant>
      <ChartView data={data} recipe={recipe(kind)} units={units} title="Monthly costs" />
    </ThemeRoot>,
  )

it.each(['BAR', 'LINE', 'AREA', 'DONUT'] as const)('draws a %s chart as a picture hidden from assistive technology, named once as an image', async (kind) => {
  view(money, kind)

  const image = screen.getByRole('img', { name: 'Monthly costs: chart' })
  expect(image.querySelector('svg')).not.toBeNull()
  expect(image.querySelector('[aria-hidden="true"]')).not.toBeNull() // the drawing says nothing itself
  expect(within(image).queryAllByRole('img', { hidden: false })).toHaveLength(0)
  expect(screen.queryByRole('table')).not.toBeInTheDocument() // the numbers are behind the toggle
})

it('lists the same numbers as a table behind a toggle, with a dash for a missing value', async () => {
  const ui = userEvent.setup()
  view(money, 'BAR')

  await ui.click(screen.getByRole('button', { name: 'Show the data as a table' }))

  const table = screen.getByRole('table', { name: 'Monthly costs' })
  expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Period', 'Fuel · HUF', 'Expenses · HUF'])
  expect(within(table).getByRole('rowheader', { name: 'Sep 2026' }).closest('tr')).toHaveTextContent(/15,500.*–/)
  await ui.click(screen.getByRole('button', { name: 'Hide the table' }))
  expect(screen.queryByRole('table')).not.toBeInTheDocument()
})

it('says so instead of drawing an empty chart', () => {
  view({ unit: 'CURRENCY', series: [{ kind: 'total', currency: 'HUF', points: [{ key: '2026-08', value: 0 }] }] }, 'LINE')

  expect(screen.getByRole('status')).toHaveTextContent('No data for this period yet.')
  expect(screen.queryByRole('img')).not.toBeInTheDocument()
})
