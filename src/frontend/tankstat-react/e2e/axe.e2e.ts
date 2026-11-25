import { expect, test } from './support/test.ts'
import { addVehicle, createUser, graphql, LAYOUT_VERSION, openVehicle, pageLoaded, signIn, signInAsNewUser } from './support/app.ts'
import { violations } from './support/axe.ts'

// axe in a real browser: what jsdom cannot judge (the colour contrast of the real styles, what is hidden after layout). The jsdom scans
// in tests/app/Accessibility*.integration.test.tsx stay; these add the pages as they are drawn, at both sizes.

test.beforeEach(async ({ page }) => signIn(page))

for (const [name, path] of [
  ['the home page', '/'],
  ['the Overview', '/admin'],
  ['the Server settings', '/admin/settings'],
  ['the Backups page', '/admin/backups'],
  ['the account page', '/account'],
  ['the People page', '/admin/people'],
] as const) {
  test(`${name} has no axe violations`, async ({ page }) => {
    await page.goto(path)
    await pageLoaded(page)
    expect(await violations(page)).toEqual([])
  })
}

test('a vehicle page and its add refuelling dialog have no axe violations', async ({ page }) => {
  const vehicle = await addVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}?tab=refuelings`)
  await pageLoaded(page)
  expect(await violations(page)).toEqual([])
  await page.getByRole('button', { name: 'Add refuelling' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'Add refuelling' })
  await expect(dialog).toBeVisible()
  expect(await violations(page, '[role="dialog"]')).toEqual([])
})

test('the tyre swap reminder, on the Tyres tab and on the home card, has no axe violations (#292)', async ({ page }) => {
  // A winter set in storage reminded from the server's (UTC) month: due whatever day the test runs.
  const vehicle = await addVehicle(page)
  await graphql(page, 'mutation($i: AddTyreSetInput!) { addTyreSet(input: $i) { vehicleId } }', {
    i: { vehicleId: vehicle.id, name: 'Continental WinterContact', season: 'WINTER', since: '2022-11-19', fitted: false, day: new Date().toISOString().slice(0, 10), reminderMonth: new Date().getUTCMonth() + 1 },
  })
  await openVehicle(page, vehicle.id, 'tyres')
  await expect(page.getByText('Time to fit the winter tyres')).toBeVisible()
  expect(await violations(page)).toEqual([])

  await page.goto('/')
  await pageLoaded(page)
  // The shared user's other cars (the other size's run among them) may show a row too: this test's card is the one looked for.
  await expect(page.getByRole('link', { name: `Open ${vehicle.name}` }).locator('xpath=ancestor::li[1]').getByText('Time to swap')).toBeVisible()
  expect(await violations(page)).toEqual([])
})

test('the tyre timeline, with a gap, a swap handle and its Show menu open, has no axe violations (#332)', async ({ page }) => {
  const day = (daysAgo: number) => new Date(Date.now() - daysAgo * 86_400_000).toISOString().slice(0, 10)
  const vehicle = await addVehicle(page)
  const addSet = 'mutation($i: AddTyreSetInput!) { addTyreSet(input: $i) { sets { set { id name } } } }'
  const set = { vehicleId: vehicle.id, replace: false, reminderMonth: 0, fitted: false, day: day(0) } // no swap note above the card, whatever the month
  await graphql(page, addSet, { i: { ...set, name: 'Michelin Primacy 4', season: 'SUMMER', since: day(900), roughDistance: 30_000 } })
  await graphql(page, addSet, { i: { ...set, name: 'Spare all-season', season: 'ALL_SEASON', since: day(400), roughDistance: 0 } })
  const sets = (await graphql<{ addTyreSet: { sets: { set: { id: string; name: string } }[] } }>(page, addSet, { i: { ...set, name: 'Continental WinterContact', season: 'WINTER', since: day(900), roughDistance: 0 } })).addTyreSet.sets
  const id = (name: string) => sets.find((s) => s.set.name === name)!.set.id
  const add = 'mutation($i: AddTyreInstallationInput!) { addTyreInstallation(input: $i) { vehicleId } }'
  const period = (name: string, from: number, until: number | null) => graphql(page, add, { i: { setId: id(name), from: day(from), until: until === null ? null : day(until) } })
  await period('Continental WinterContact', 300, 200)
  await period('Spare all-season', 200, 180) // a swap, then two weeks nobody recorded
  await period('Michelin Primacy 4', 166, null)
  await openVehicle(page, vehicle.id, 'tyres')
  const timeline = page.locator('[data-timeline]')
  await expect(timeline.getByRole('button', { name: /Move the swap$/ }).first()).toBeVisible()
  await timeline.getByRole('button', { name: 'Show' }).click()
  await expect(timeline.getByRole('checkbox', { name: 'Gaps' })).toBeVisible()
  // The card alone: the tyre log under it has a finding of its own (its rows' names, #346), which is not the timeline's.
  expect(await violations(page, '[data-timeline]')).toEqual([])
})

test('a vehicle\'s dashboard, shown and being arranged, has no axe violations', async ({ page }) => {
  const vehicle = await addVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}`)
  await pageLoaded(page)
  await expect(page.getByRole('heading', { name: 'Monthly costs' })).toBeVisible()
  expect(await violations(page)).toEqual([])
  await page.getByRole('button', { name: 'Customize' }).click()
  await expect(page.getByRole('button', { name: 'Save dashboard' })).toBeVisible()
  expect(await violations(page)).toEqual([])
  await page.getByRole('button', { name: 'Add widget' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add widget' })
  await expect(dialog).toBeVisible()
  await expect(page.locator('.MuiDialog-container')).toHaveCSS('opacity', '1') // faded in: axe reads the colours as they are drawn
  expect(await violations(page, '[role="dialog"]')).toEqual([])
  // The builder (#275): a key figure from scratch, then as a chart.
  await dialog.getByRole('button', { name: /^From scratch/ }).click()
  await expect(dialog.getByRole('combobox', { name: 'What it shows' })).toBeVisible()
  expect(await violations(page, '[role="dialog"]')).toEqual([])
  await dialog.locator('label', { hasText: /^Chart$/ }).click()
  await expect(dialog.getByRole('combobox', { name: 'What to measure' })).toBeVisible()
  expect(await violations(page, '[role="dialog"]')).toEqual([])
})

// #319: a part that fades in inside the scanned one is waited for. The builder's period panel fades in (`tk-fade`); stretched to 3 s
// here, a scan right after *From scratch* would measure its text dimmed (a contrast violation) unless the scan waits for it to end.
test('a scan waits for what fades in inside the part it scans (#319)', async ({ page }) => {
  const vehicle = await addVehicle(page)
  await page.goto(`/vehicles/${vehicle.id}`)
  await pageLoaded(page)
  await expect(page.getByRole('heading', { name: 'Monthly costs' })).toBeVisible()
  await page.getByRole('button', { name: 'Customize' }).click()
  await expect(page.getByRole('button', { name: 'Save dashboard' })).toBeVisible()
  await page.getByRole('button', { name: 'Add widget' }).click()
  const dialog = page.getByRole('dialog', { name: 'Add widget' })
  await expect(dialog).toBeVisible()
  await expect(page.locator('.MuiDialog-container')).toHaveCSS('opacity', '1')
  // The fade stretched; and an animation that never ends (as a spinner's) on the dialog's title, which the scan must not wait for.
  await page.addStyleTag({ content: '.tk-fade { animation-duration: 3s !important; } @keyframes zz-endless { to { transform: none; } } [role="dialog"] h2 { animation: zz-endless 1s linear infinite; }' })
  await dialog.getByRole('button', { name: /^From scratch/ }).click()
  await expect(dialog.getByRole('combobox', { name: 'What it shows' })).toBeVisible()
  const fading = () => page.evaluate(() => document.getAnimations().some((a) => (a as CSSAnimation).animationName === 'tk-fade' && a.playState === 'running'
    && ((a.effect as KeyframeEffect).target as Element).closest('[role="dialog"]') !== null))
  expect(await fading()).toBe(true) // the panel is still fading in when the scan is asked for
  expect(await violations(page, '[role="dialog"]')).toEqual([])
  // The scan ran only once it had faded in: a desktop's axe does not judge the dimmed pick, so this says it at both sizes.
  expect(await fading()).toBe(false)
})

// The solid fills (#235): a primary button under the pointer, and a red confirmation at rest and under it, in both schemes; a hover is
// a pointer's, so on a desktop only. Each scan waits for the fill to be the hover's (MUI's 250 ms transition), as it is drawn.
for (const scheme of ['dark', 'light'] as const) {
  test(`a hovered primary button, a red confirmation and the bell's badge reach 4.5:1, ${scheme}`, async ({ page, request, isMobile }) => {
    test.skip(isMobile, 'a hover is a pointer\'s')
    if (scheme === 'light') await page.addInitScript(() => localStorage.setItem('tankstat.colorMode', 'light'))
    // A user of its own, whom someone else grants access to their vehicle: the bell then has an unread count.
    const me = await signInAsNewUser(page)
    const owner = await createUser()
    await signIn(request, owner)
    const theirs = await addVehicle(request)
    await graphql(request, 'mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }', { i: { vehicleId: theirs.id, userId: me.id, level: 'EDIT' } })
    const vehicle = await addVehicle(page)
    await page.goto('/')
    await pageLoaded(page)
    // The fills are the same in both schemes, so only the class tells that the scheme asked for is the one drawn.
    await expect(page.locator('html')).toHaveClass(scheme === 'light' ? /\blight\b/ : /\bdark\b/)
    // The badge is aria-hidden, so axe skips it: its fill is checked as drawn, the red's solid one (white on it is 4.88:1).
    await expect(page.getByRole('button', { name: /^Notifications, \d+ unread$/ })).toBeVisible()
    await expect(page.locator('.MuiBadge-badge')).toHaveCSS('background-color', 'rgb(202, 63, 68)')
    // The add vehicle dialog's submit is a contained primary button (the toolbar's own is a soft one).
    await page.getByRole('button', { name: 'Add vehicle' }).first().click()
    const dialog = page.getByRole('dialog', { name: 'Add vehicle' })
    await dialog.getByLabel('Name').fill('Hovered')
    const add = dialog.getByRole('button', { name: 'Add vehicle' })
    await add.hover()
    await expect(add).toHaveCSS('background-color', 'rgb(56, 89, 199)') // the primary's solid hover, #3859c7
    expect(await violations(page, '[role="dialog"]')).toEqual([])
    await dialog.getByRole('button', { name: 'Cancel' }).click()
    await expect(dialog).toBeHidden()

    await openVehicle(page, vehicle.id, 'details')
    await page.getByRole('region', { name: 'Delete this vehicle' }).getByRole('button', { name: 'Move to trash' }).click()
    const confirm = page.getByRole('alertdialog', { name: `Move ${vehicle.name} to the trash?` }).getByRole('button', { name: 'Move to trash' })
    await page.mouse.move(0, 0)
    await expect(confirm).toHaveCSS('background-color', 'rgb(202, 63, 68)') // the red's solid fill, #ca3f44
    expect(await violations(page, '[role="alertdialog"]')).toEqual([])
    await confirm.hover()
    await expect(confirm).toHaveCSS('background-color', 'rgb(183, 58, 62)') // and its hover, #b73a3e
    expect(await violations(page, '[role="alertdialog"]')).toEqual([])
  })
}

test('a trend tile\'s period popover has no axe violations (#230)', async ({ page }) => {
  const vehicle = await addVehicle(page)
  await graphql(page, 'mutation($i: SaveDashboardLayoutInput!) { saveDashboardLayout(input: $i) }', {
    i: { vehicleId: vehicle.id, personal: false, tiles: [{ id: 'spendingTrend', x: 0, y: 0, w: 1, h: 4, pinned: false }], hidden: [], layoutVersion: LAYOUT_VERSION }, // refused without (#273)
  })
  await page.goto(`/vehicles/${vehicle.id}`)
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Customize' }).click() // the chip is on the tile's strip while arranging (#260)
  await page.getByRole('button', { name: 'Spending period: 12 full months. Change it' }).click()
  const popover = page.getByRole('dialog', { name: 'Period of Spending' })
  await expect(popover.getByTestId('trend-timeline')).toBeVisible()
  expect(await violations(page, '[role="dialog"]')).toEqual([])
  await popover.getByRole('tab', { name: 'Last' }).click()
  await expect(popover.getByRole('switch', { name: 'Whole weeks, Monday to Sunday' })).toBeVisible()
  expect(await violations(page, '[role="dialog"]')).toEqual([])
})

test('the add vehicle dialog has no axe violations', async ({ page }) => {
  await page.goto('/')
  await pageLoaded(page)
  await page.getByRole('button', { name: 'Add vehicle' }).first().click()
  await expect(page.getByRole('dialog', { name: 'Add vehicle' })).toBeVisible()
  expect(await violations(page, '[role="dialog"]')).toEqual([])
})
