import { request, type APIRequestContext } from '@playwright/test'
import { expect, test } from './support/test.ts'
import { ADMIN, pageLoaded, tag } from './support/app.ts'
import { violations } from './support/axe.ts'
import { expectSoundLayout } from './support/layout.ts'
import { restoreServer } from './support/setup.ts'

// Restore cars (#340): an administrator backs up, a user's car is purged, the administrator puts it back from the restore point, and the
// user finds it again with its refuellings and the notification. A Standalone server of this size's own: a run holds SQLite's lock to
// write, and the restore needs people who sign in.

const api = async <T>(client: APIRequestContext, query: string, variables?: Record<string, unknown>) => {
  const response = await client.post('/graphql', { data: { query, variables } })
  const body = (await response.json()) as { data?: T; errors?: { message: string }[] }
  expect(body.errors ?? [], `GraphQL errors for ${query}`).toEqual([])
  return body.data as T
}
const LOGIN = 'mutation($i: LoginInput!) { login(input: $i) { id } }'

test('an administrator restores a purged car of a user from a restore point, and the user finds it again', async ({ page }) => {
  const server = restoreServer()
  const id = tag()
  const owner = { email: `owner-${id}@example.com`, name: `Owner ${id}`, password: `password-${id}` }
  const car = `Golf ${id}`

  // The user and their car, made through GraphQL: two fill-ups.
  const admin = await request.newContext({ baseURL: server.url })
  const user = await request.newContext({ baseURL: server.url })
  try {
    await api(admin, LOGIN, { i: ADMIN })
    const created = await api<{ createUser: { reset: { token: string } } }>(admin, 'mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }', {
      i: { email: owner.email, displayName: owner.name, isAdmin: false },
    })
    await api(user, 'mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }', {
      i: { token: created.createUser.reset.token, newPassword: owner.password, newPasswordConfirmation: owner.password },
    })
    await api(user, LOGIN, { i: { email: owner.email, password: owner.password } })
    const vehicle = (await api<{ addVehicle: { id: string } }>(user, 'mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }', { i: { name: car, fuelType: 'PETROL' } })).addVehicle.id
    for (const [date, odometer] of [['2026-01-15', 1000], ['2026-02-15', 1600]] as const)
      await api(user, 'mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }', {
        i: { vehicleId: vehicle, date, volume: 40, totalCost: 24000, currency: 'HUF', odometer, isFullTank: true },
      })

    // The administrator backs up on the Backups page.
    await page.goto(`${server.url}/`)
    await page.request.post(`${server.url}/graphql`, { data: { query: LOGIN, variables: { i: ADMIN } } })
    await page.goto(`${server.url}/admin/backups`)
    await pageLoaded(page)
    await page.getByRole('button', { name: 'Back up now' }).click()
    const written = page.getByText(/^The last backup was written/)
    await expect(written).toBeVisible({ timeout: 30_000 })
    const file = /: (\S+), [^,]+\.$/.exec((await written.textContent()) ?? '')?.[1]
    expect(file, 'the file named in the last backup line').toBeTruthy()

    // The user's car goes for good.
    await api(user, 'mutation($id: UUID!) { deleteVehicle(id: $id) { id } }', { id: vehicle })
    await api(user, 'mutation { emptyTrash }')

    // Restore cars, from that backup's row.
    const escaped = file!.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    await page.getByRole('link', { name: new RegExp(`^Restore cars from the backup of .*\\(${escaped}\\)$`) }).click()
    await expect(page.getByRole('heading', { level: 1, name: /^Restore cars from the backup of/ })).toBeVisible({ timeout: 30_000 })
    const card = page.getByRole('region', { name: `${car} · ${owner.name}'s` })
    await expect(card).toContainText('Not here any more. It comes back with its entries, owner and sharing (not shared).')
    await expectSoundLayout(page)
    expect(await violations(page)).toEqual([])
    await card.getByRole('checkbox').check()
    await page.getByRole('button', { name: 'Review' }).click()

    const reviewed = page.getByRole('region', { name: car })
    await expect(reviewed).toContainText(`It comes back whole to ${owner.name}: 2 refuellings, under their own ids, logged by who logged them.`)
    await expect(page.getByText(`${owner.name} and everyone their cars are shared with are told afterwards.`)).toBeVisible()
    await expectSoundLayout(page)
    await page.getByRole('button', { name: 'Restore 1 car' }).click()
    await expect(page.getByText('Restore finished: 1 car.')).toBeVisible({ timeout: 60_000 })
    await expect(page.getByText(`It is ${owner.name}'s again.`)).toBeVisible()
    await expectSoundLayout(page)

    // The user signs in on this device: the car is back with its refuellings, and the bell says so.
    await page.context().clearCookies()
    await page.request.post(`${server.url}/graphql`, { data: { query: LOGIN, variables: { i: { email: owner.email, password: owner.password } } } })
    await page.goto(`${server.url}/vehicles/${vehicle}`)
    await expect(page.getByRole('heading', { level: 1, name: car })).toBeVisible()
    await page.getByRole('tab', { name: 'Refuelings' }).click()
    await expect(page.getByRole('row').filter({ hasText: /2026/ })).toHaveCount(2) // both fill-ups, whatever columns this size shows
    await page.getByRole('button', { name: /^Notifications, \d+ unread$/ }).click()
    const bell = page.getByRole('dialog', { name: 'Notifications' })
    await expect(bell.getByText(new RegExp(`^.+ restored ${car} from the backup of .+\\. It is yours again\\.$`))).toBeVisible()
  } finally {
    await admin.dispose()
    await user.dispose()
  }
})
