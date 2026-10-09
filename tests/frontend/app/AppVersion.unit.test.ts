import { expect, it } from 'vitest'
import { APP_VERSION, appVersion, DEV_VERSION } from '../../../src/frontend/appVersion.ts'

it('shows the version a build was given, pre-release label included', () => {
  expect(appVersion('1.4.0')).toBe('1.4.0')
  expect(appVersion('0.12.0-preview')).toBe('0.12.0-preview')
  expect(appVersion('0.0.0-dev.abc1234')).toBe('0.0.0-dev.abc1234')
})

it('leaves out the build metadata, like the API does', () => {
  expect(appVersion('1.4.0+6e1f0c2')).toBe('1.4.0')
  expect(appVersion(' 1.4.0-rc.1+6e1f0c2 ')).toBe('1.4.0-rc.1')
})

it('falls back to the development version when the build was given none', () => {
  expect(appVersion(undefined)).toBe(DEV_VERSION)
  expect(appVersion('')).toBe(DEV_VERSION)
  expect(appVersion('  ')).toBe(DEV_VERSION)
  expect(appVersion('+6e1f0c2')).toBe(DEV_VERSION)
})

it('is baked into the build by the Vite config', () => {
  expect(typeof __APP_VERSION__).toBe('string')
  expect(APP_VERSION).toBe(__APP_VERSION__)
  expect(APP_VERSION).toBe(appVersion(APP_VERSION))
})
