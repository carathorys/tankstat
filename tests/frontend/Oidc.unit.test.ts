import { expect, it } from 'vitest'
import { oidcLoginUrl, SIGNED_OUT_KEY, signedOut, wordedReason } from '../../src/frontend/auth/oidc.ts'

it('the login URL brings the user back to the page they opened, without the failure markers', () => {
  expect(oidcLoginUrl('/', '')).toBe('/auth/oidc/login?returnUrl=%2F')
  expect(oidcLoginUrl('/vehicles/abc', '?tab=refuelings')).toBe('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
  expect(oidcLoginUrl('/vehicles/abc', '?tab=refuelings&signIn=failed&reason=access_denied')).toBe('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
  expect(oidcLoginUrl('/', '?signIn=failed')).toBe('/auth/oidc/login?returnUrl=%2F')
})

it('only the reasons with their own text are worded', () => {
  expect(wordedReason('account_disabled')).toBe('account_disabled')
  expect(wordedReason('access_denied')).toBe('access_denied')
  expect(wordedReason('callback_rejected')).toBeNull()
  expect(wordedReason(null)).toBeNull()
})

it('"signed out" is remembered for the tab until it is cleared', () => {
  expect(signedOut.isSet()).toBe(false)
  signedOut.mark()
  expect(signedOut.isSet()).toBe(true)
  expect(window.sessionStorage.getItem(SIGNED_OUT_KEY)).toBe('1')
  signedOut.clear()
  expect(signedOut.isSet()).toBe(false)
})
