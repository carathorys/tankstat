import { screen, waitFor, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { connectivity } from '../../src/offline/connectivity.ts'
import { deviceData } from '../../src/offline/deviceData.ts'
import { outbox } from '../../src/offline/outbox.ts'
import { check, setup, setupAccessibilityTests } from '../support/accessibility.tsx'
import { answered, server } from '../support/server.ts'
import { fakeTyres, tyreSummer, tyreWinter } from '../support/tyres.ts'

// The recurring expenses (table, dialogs, selection) and the home cards with their quick actions.
setupAccessibilityTests()

it('the recurring expenses table and its add dialog are labelled and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=recurring')
  const table = await screen.findByRole('table', { name: 'Recurring expenses' })
  within(table).getByText('Tyres')
  await waitFor(() => expect(table.querySelectorAll('[data-limit]')).toHaveLength(3)) // the gauges (one schedule has two), once their chunk is in
  await check(document.body)

  await ui.click(screen.getByRole('button', { name: 'Add recurring expense' }))
  const adding = await screen.findByRole('dialog', { name: 'Add recurring expense' })
  expect(adding).toHaveAccessibleDescription(/comes back again and again/)
  await check(document.body)
})

// Their own test: each scan of the whole page takes a while on CI.
it('the edit and Mark as done dialogs of a recurring expense are labelled and free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=recurring')
  server.use(...fakeTyres([tyreSummer, tyreWinter]).handlers) // the Done dialog's tyre details (#290)
  await screen.findByRole('table', { name: 'Recurring expenses' })

  await ui.click(screen.getByRole('button', { name: 'Edit the recurring expense Tyres' }))
  const editing = await screen.findByRole('dialog', { name: 'Edit recurring expense' })
  await within(editing).findByLabelText('Title')
  await check(document.body)
  await ui.click(within(editing).getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(screen.getByRole('button', { name: 'Mark Tyres as done' }))
  const done = await screen.findByRole('dialog', { name: 'Mark as done: Tyres' })
  await within(done).findByLabelText('Currency')
  expect(within(done).getByRole('group', { name: 'Done at this visit' })).toBeInTheDocument() // the schedules to tick, as labelled checkboxes
  await ui.click(await within(done).findByRole('switch', { name: 'Tyre details' }))
  await within(done).findByRole('group', { name: 'Tyre details' })
  await check(document.body) // with its tyre details open
})

it('the selection column of the recurring expenses and its Mark selected as done dialog are free of violations', async () => {
  const { ui } = setup('/vehicles/v1?tab=recurring')
  await screen.findByRole('table', { name: 'Recurring expenses' })

  await ui.click(screen.getByRole('checkbox', { name: 'Select all recurring expenses' }))
  await check(document.body)
  await ui.click(screen.getByRole('button', { name: /selected as done/ }))
  const many = await screen.findByRole('dialog', { name: 'Mark as done' })
  await within(many).findByLabelText('Currency')
  await check(document.body)
})

it('the home page card lists what needs attention in a labelled list, free of violations', async () => {
  setup('/')
  const list = await screen.findByRole('list', { name: 'Needs attention' })

  expect(within(list).getByText(/Tyres · 300 km over/)).toBeInTheDocument()
  // The swap reminder's row (#292): in words, its two buttons naming the set (and the car).
  expect(within(list).getByText('Time to swap')).toBeInTheDocument()
  expect(within(list).getByRole('button', { name: 'Swap to Continental WinterContact on Octavia' })).toBeInTheDocument()
  expect(within(list).getByRole('button', { name: 'Not this season: Continental WinterContact' })).toBeInTheDocument()
  await check(document.body)
})

it('the quick actions on a home card have names with the vehicle in them, on a desktop and a phone, free of violations', async () => {
  const { view } = setup('/')
  const card = within((await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!)
  expect(card.getByRole('button', { name: 'Refuel Octavia' })).toBeInTheDocument()
  expect(card.getByRole('button', { name: 'Expense for Octavia' })).toBeInTheDocument()
  expect(card.getByRole('button', { name: 'Mark Tyres of Octavia as done' })).toBeInTheDocument()
  await check(view.container)
  view.unmount()

  const phone = setup('/', 'phone')
  await screen.findByRole('link', { name: 'Open Octavia' })
  await check(phone.view.container)
})

it('the dialogs opened from a home card are labelled, described and free of violations', async () => {
  const { ui } = setup('/')
  const card = within((await screen.findByRole('link', { name: 'Open Octavia' })).closest('li')!)

  await ui.click(card.getByRole('button', { name: 'Refuel Octavia' }))
  let dialog = await screen.findByRole('dialog', { name: 'Add refuelling' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  expect(dialog).toHaveAccessibleDescription(/Enter what you filled up/)
  await check(document.body)
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(card.getByRole('button', { name: 'Expense for Octavia' }))
  dialog = await screen.findByRole('dialog', { name: 'Add expense' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await check(document.body)
  await ui.keyboard('{Escape}')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

  await ui.click(card.getByRole('button', { name: 'Mark Tyres of Octavia as done' }))
  dialog = await screen.findByRole('dialog', { name: 'Mark as done: Tyres' })
  await waitFor(() => expect(within(dialog).getByLabelText('Currency')).toHaveValue('HUF'))
  await check(document.body)
})

it("a visit's edit on Waiting to sync that leaves changes behind: its note and its confirmation are labelled and free of violations (#330)", async () => {
  const { ui } = setup('/vehicles/v1?tab=recurring')
  await screen.findByRole('table', { name: 'Recurring expenses' }) // its schedules kept on the device, for the Done dialog offline
  await deviceData.settled()
  await answered()
  connectivity.failed() // kept here, nothing sent meanwhile
  await (await deviceData.rows())!.putVehicles([{ id: 'v1', name: 'Octavia', logAccess: 'DELETE', version: 1, units: { distance: 'KILOMETERS', volume: 'LITERS' } } as never]) // downloaded: its changes can be edited
  await outbox.enqueue({
    id: 'm1', entity: 'recurring', action: 'markDone', vehicleId: 'v1', targetId: 'e7', targetIds: ['rc2'],
    input: { ids: ['rc2'], date: '2026-10-05', odometer: 99_000, amount: 250, currency: 'EUR', title: 'Tyres', category: 'Tyres', expenseId: 'e7', photoIds: [] },
  })
  await outbox.enqueue({ id: 'x1', entity: 'expenses', action: 'update', vehicleId: 'v1', targetId: 'e7', input: { id: 'e7', date: '2026-10-05', title: 'Tyres', amount: 250, currency: 'EUR' } })
  await ui.click(await screen.findByRole('link', { name: /^Waiting to sync/ }))

  await screen.findByRole('heading', { name: 'Waiting to sync', level: 1 })
  await ui.click(await screen.findByRole('button', { name: /^Edit the change: Marked as done/ }, { timeout: 10_000 }))
  const dialog = await screen.findByRole('dialog', { name: 'Edit the change' })
  await waitFor(() => expect(within(dialog).getByLabelText('Amount (optional)')).toHaveValue('250'))
  await ui.clear(within(dialog).getByLabelText('Amount (optional)'))
  expect((await within(dialog).findByText(/^Saving also removes 1 change waiting on this device/)).closest('[role="status"]')).not.toBeNull() // said politely
  await check(document.body)

  await ui.click(within(dialog).getByRole('button', { name: 'Save the change' }))
  const ask = await screen.findByRole('alertdialog', { name: 'Remove this change too?' })
  expect(ask).toHaveAccessibleDescription(/It cannot be brought back\.$/)
  await check(ask)
})
