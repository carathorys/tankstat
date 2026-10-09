import { createInstance } from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { i18nOptions } from '../../../../src/frontend/i18n/index.ts'

/** A fresh i18next with the app's own options and detection, as a browser that reports `language` starts it. */
async function detectWith(language: string) {
  vi.spyOn(navigator, 'language', 'get').mockReturnValue(language)
  vi.spyOn(navigator, 'languages', 'get').mockReturnValue([language])
  const instance = createInstance().use(LanguageDetector)
  await instance.init(i18nOptions())
  return instance
}

afterEach(() => vi.restoreAllMocks())

describe('the detected language', () => {
  it('is cleaned to a valid tag, keeps the language it resolves to, and is stored clean', async () => {
    const i18n = await detectWith('en-US@posix')

    expect(i18n.language).toBe('en-US')
    expect(i18n.resolvedLanguage).toBe('en')
    expect(window.localStorage.getItem('tankstat.language')).toBe('en-US')
    expect(() => new Intl.NumberFormat(i18n.language)).not.toThrow()
  })

  it('a bad one stored before is repaired on the next start', async () => {
    window.localStorage.setItem('tankstat.language', 'hu_HU.UTF-8')

    const i18n = await detectWith('en-US')

    expect(i18n.language).toBe('hu-HU') // the stored choice still wins, made valid
    expect(i18n.languages).toContain('hu') // what it resolves to once the Hungarian texts load (not loaded in this test)
    expect(window.localStorage.getItem('tankstat.language')).toBe('hu-HU')
  })

  it('a locale that names no language falls back to English', async () => {
    const i18n = await detectWith('C')

    expect(i18n.language).toBe('en')
  })
})
