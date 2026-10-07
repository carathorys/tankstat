import { afterEach, expect, it, vi } from 'vitest'
import { COLOR_MODE_KEY } from '../../../src/frontend/theme/colorMode.ts'
import { initSchemeScript, initSchemeStyle, THEME_COLOR } from '../../../src/frontend/theme/initScheme.ts'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** Runs index.html's start-up script on a page with the theme-color meta, on a device whose own scheme is `device`. */
function start(stored: string | null, device: 'light' | 'dark' = 'dark') {
  document.documentElement.className = ''
  document.head.innerHTML = '<meta name="theme-color" content="#1f2a6b" />'
  if (stored !== null) window.localStorage.setItem(COLOR_MODE_KEY, stored)
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === `(prefers-color-scheme: ${device})` }))
  new Function(initSchemeScript())()
  return { scheme: document.documentElement.className, bar: document.querySelector('meta[name="theme-color"]')?.getAttribute('content') }
}

it('starts dark when nothing was chosen (or something it does not know), the browser bar indigo', () => {
  expect(start(null)).toEqual({ scheme: 'dark', bar: THEME_COLOR.dark })
  expect(start('sepia', 'light')).toEqual({ scheme: 'dark', bar: THEME_COLOR.dark })
})

it('applies a light choice before anything paints, the browser bar too', () => {
  expect(start('light')).toEqual({ scheme: 'light', bar: THEME_COLOR.light })
  expect(start('dark', 'light')).toEqual({ scheme: 'dark', bar: THEME_COLOR.dark })
})

it('follows the device when the choice is system', () => {
  expect(start('system', 'light')).toEqual({ scheme: 'light', bar: THEME_COLOR.light })
  expect(start('system', 'dark')).toEqual({ scheme: 'dark', bar: THEME_COLOR.dark })
})

it('never breaks the page when the browser keeps nothing (storage blocked)', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('blocked')
  })
  expect(() => start(null)).not.toThrow()
})

it('colours the page from the first paint with rules the theme overrides once it is there', () => {
  const style = initSchemeStyle()
  expect(style).toContain(':where(body){margin:0;background-color:#111110}') // the dark theme's background
  expect(style).toContain(':where(html.light body){background-color:')
  expect(style).not.toMatch(/(^|})(html|body)\{/) // every selector weightless
})
