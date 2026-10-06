import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'
import { i18n, initI18n } from '../../src/frontend/i18n/index.ts'
import { installPrompt } from '../../src/frontend/pwa/installPrompt.ts'

await initI18n('en')

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
