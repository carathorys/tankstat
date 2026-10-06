import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach } from 'vitest'
import { i18n, initI18n } from '../../src/frontend/i18n/index.ts'
import { installPrompt } from '../../src/frontend/pwa/installPrompt.ts'

await initI18n('en')

// Pages are lazy: the first test of a file waits for App and its page to load, which with coverage on (CI) can take
// well over the 1 s default of findBy*/waitFor. Stays below Vitest's 5 s test timeout, so a miss still names the element.
configure({ asyncUtilTimeout: 3000 })

afterEach(async () => {
  cleanup()
  window.localStorage.clear()
  window.sessionStorage.clear()
  installPrompt.reset()
  await i18n.changeLanguage('en')
})

// jsdom lacks a few browser APIs that Radix primitives (Select, popper) rely on.
Element.prototype.hasPointerCapture ??= () => false
Element.prototype.setPointerCapture ??= () => {}
Element.prototype.releasePointerCapture ??= () => {}
Element.prototype.scrollIntoView ??= () => {}
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
}
