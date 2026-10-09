import { describe, expect, it } from 'vitest'
import { validLocale } from '../../../../src/frontend/i18n/locale.ts'

describe('validLocale', () => {
  it('keeps a valid tag, in its canonical spelling', () => {
    expect(validLocale('hu-HU')).toBe('hu-HU')
    expect(validLocale('EN-us')).toBe('en-US')
    expect(validLocale('en-u-ca-gregory')).toBe('en-u-ca-gregory')
  })

  it('makes a valid tag of a POSIX locale, and cuts back what is not valid', () => {
    expect(validLocale('en-US@posix')).toBe('en-US')
    expect(validLocale('en_GB')).toBe('en-GB')
    expect(validLocale('en_GB.UTF-8@euro')).toBe('en-GB')
    expect(validLocale('zz-ZZ-x')).toBe('zz-ZZ')
  })

  it('gives the fallback for a locale that names no language, and for nothing', () => {
    for (const tag of ['C', 'C.UTF-8', 'POSIX', '', '   ', null, undefined, '@@']) expect(validLocale(tag)).toBe('en')
    expect(validLocale('C', 'hu')).toBe('hu')
  })

  it('always gives what Intl accepts', () => {
    for (const tag of ['en-US@posix', 'en_GB.UTF-8@euro', 'zz-ZZ-x', 'C', '']) expect(() => new Intl.NumberFormat(validLocale(tag)).format(1)).not.toThrow()
  })
})
