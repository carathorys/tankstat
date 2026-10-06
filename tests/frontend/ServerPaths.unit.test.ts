import { expect, it } from 'vitest'
import { isServerPath, SERVER_PATHS } from '../../src/frontend/pwa/serverPaths.ts'

it('the worker leaves the API, sign-in, pictures and uploads to the server', () => {
  expect(isServerPath('/graphql')).toBe(true)
  expect(isServerPath('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc')).toBe(true)
  expect(isServerPath('/auth/oidc/callback?code=x&state=y')).toBe(true)
  expect(isServerPath('/media/0a1b2c3d4e5f60718293a4b5c6d7e8f9')).toBe(true)
  expect(isServerPath('/imports/fuelio')).toBe(true)
})

it('the app shell answers every page of the app', () => {
  expect(isServerPath('/')).toBe(false)
  expect(isServerPath('/vehicles/abc?tab=refuelings')).toBe(false)
  expect(isServerPath('/import')).toBe(false) // the SPA route, not the upload endpoint
  expect(isServerPath('/account')).toBe(false)
})

it('is the list the build hands to Workbox', () => {
  expect(SERVER_PATHS.map(String)).toEqual(['/^\\/graphql/', '/^\\/auth\\//', '/^\\/media\\//', '/^\\/imports\\//'])
})
