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

const tableOf = async (data: ChartData) => {
  const ui = userEvent.setup()
  view(data, 'LINE')
  await ui.click(screen.getByRole('button', { name: 'Show the data as a table' }))
  return screen.getByRole('table', { name: 'Monthly costs' })
}

it.each([
  ['VOLUME', 42.5, '42.5 L'],
  ['DISTANCE', 1234, '1,234 km'],
  ['CONSUMPTION', 6.75, '6.75 L/100 km'],
  ['COUNT', 3, '3'],
] as const)('writes %s values in the vehicle\'s units in the table', async (unit, value, text) => {
  const table = await tableOf({ unit, series: [{ kind: 'total', currency: null, points: [{ key: '2026-09', value }] }] })

  expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Period', 'Total'])
  expect(within(table).getByRole('rowheader', { name: 'Sep 2026' }).closest('tr')).toHaveTextContent(`Sep 2026${text}`)
})

it('lists a chart by category with the expenses that have none as Uncategorised', async () => {
  const ui = userEvent.setup()
  render(
    <ThemeRoot instant>
      <ChartView
        data={{ unit: 'CURRENCY', series: [{ kind: 'expenses', currency: 'HUF', points: [{ key: 'Service', value: 35000 }, { key: '', value: 1500 }] }] }}
        recipe={{ kind: 'DONUT', metric: 'EXPENSE_COST', grouping: 'CATEGORY', stacked: false }}
        units={units}
        title="Expenses by category"
      />
    </ThemeRoot>,
  )

  await ui.click(screen.getByRole('button', { name: 'Show the data as a table' }))

  const table = screen.getByRole('table', { name: 'Expenses by category' })
  expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Category', 'Expenses · HUF'])
  expect(within(table).getAllByRole('rowheader').map((h) => h.textContent)).toEqual(['Service', 'Uncategorised'])
})

it('names a series of a kind it does not know plainly, and writes an amount without a currency as a bare number', async () => {
  const table = await tableOf({ unit: 'CURRENCY', series: [{ kind: 'price', currency: null, points: [{ key: '2026-09', value: 12000 }] }] })

  expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Period', 'Value'])
  expect(within(table).getByRole('rowheader', { name: 'Sep 2026' }).closest('tr')).toHaveTextContent('Sep 202612,000.00')
})
