import { expect, type Page } from '@playwright/test'

/** The bootstrap administrator scripts/test-e2e.sh starts the app with. */
export const ADMIN = { email: 'root@example.com', password: 'initial-password-1' }

/**
 * One GraphQL request as the page's signed-in user (`page.request` shares the context's cookies). Fails the test on an error, so a
 * test's own data is never silently missing.
 */
export async function graphql<T = Record<string, unknown>>(page: Page, query: string, variables?: Record<string, unknown>): Promise<T> {
  const response = await page.request.post('/graphql', { data: { query, variables } })
  const body = (await response.json()) as { data?: T; errors?: { message: string }[] }
  expect(body.errors ?? [], `GraphQL errors for ${query}`).toEqual([])
  return body.data as T
}

/** Signs the page's context in as the administrator: the session cookies land in the context, so `page.goto` is signed in. */
export async function signIn(page: Page) {
  const data = await graphql<{ login: { id: string } }>(page, 'mutation($i: LoginInput!) { login(input: $i) { id } }', { i: ADMIN })
  expect(data.login.id).toBeTruthy()
}

/**
 * A vehicle of the test's own. The suite shares one server and its workers run side by side, so a test makes what it needs and never
 * counts what others made.
 */
export async function addVehicle(page: Page, name = `Car ${crypto.randomUUID().slice(0, 8)}`) {
  const data = await graphql<{ addVehicle: { id: string; name: string } }>(
    page,
    'mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id name } }',
    { i: { name, licensePlate: 'E2E-001', fuelType: 'PETROL' } },
  )
  return data.addVehicle
}

/** Waits until a page has loaded: its heading is there and no loading message is left. */
export async function pageLoaded(page: Page) {
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(page.getByText(/^Loading/)).toHaveCount(0)
}
