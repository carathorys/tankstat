import { expect, it } from 'vitest'
import { currencyCodes, currencyMatches, currencyName } from '../../../src/frontend/forms/currencies.ts'

it('lists every currency the browser knows, the vehicle\'s usual one first', () => {
  const codes = currencyCodes('huf')
  expect(codes[0]).toBe('HUF')
  expect(codes).toContain('EUR')
  expect(codes.filter((c) => c === 'HUF')).toHaveLength(1)
  expect(currencyCodes('XYZ')[0]).not.toBe('XYZ') // an unknown one is not made up
})

it('names a currency in the UI language, and finds it by its code or its name', () => {
  expect(currencyName('HUF', 'en')).toBe('Hungarian Forint')
  expect(currencyName('HUF', 'hu')).toBe('magyar forint')
  expect(currencyMatches('HUF', 'Hungarian Forint', 'hu')).toBe(true) // the code, whatever the case
  expect(currencyMatches('HUF', 'Hungarian Forint', 'forint')).toBe(true)
  expect(currencyMatches('EUR', 'Euro', 'forint')).toBe(false)
})
