import { expect, it } from 'vitest'
import { failureText, oidcLoginUrl, SIGNED_OUT_KEY, signedOut } from '../../src/frontend/auth/oidc.ts'

it('the login URL brings the user back to the page they opened, without the failure markers', () => {
  expect(oidcLoginUrl('/', '')).toBe('/auth/oidc/login?returnUrl=%2F')
  expect(oidcLoginUrl('/vehicles/abc', '?tab=refuelings')).toBe('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
  expect(oidcLoginUrl('/vehicles/abc', '?tab=refuelings&signIn=failed&reason=access_denied')).toBe('/auth/oidc/login?returnUrl=%2Fvehicles%2Fabc%3Ftab%3Drefuelings')
  expect(oidcLoginUrl('/', '?signIn=failed')).toBe('/auth/oidc/login?returnUrl=%2F')
})

it('only the reasons with a text of their own get it; the rest share the general one', () => {
  expect(failureText('account_disabled')).toBe('errors.auth.accountDisabled')
  expect(failureText('access_denied')).toBe('auth.failedReasons.access_denied')
  expect(failureText('callback_rejected')).toBe('auth.failedText')
  expect(failureText('toString')).toBe('auth.failedText') // not a key of the map, whatever the prototype says
  expect(failureText(null)).toBe('auth.failedText')
})

it('"signed out" is remembered for the tab until it is cleared', () => {
  expect(signedOut.isSet()).toBe(false)
  signedOut.mark()
  expect(signedOut.isSet()).toBe(true)
  expect(window.sessionStorage.getItem(SIGNED_OUT_KEY)).toBe('1')
  signedOut.clear()
  expect(signedOut.isSet()).toBe(false)
})
