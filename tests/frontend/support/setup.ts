import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { MotionGlobalConfig } from 'motion/react'
import { afterEach } from 'vitest'
import { i18n, initI18n } from '../../../src/frontend/i18n/index.ts'
import { connectivity } from '../../../src/frontend/offline/connectivity.ts'
import { appUpdate } from '../../../src/frontend/pwa/appUpdate.ts'
import { installPrompt } from '../../../src/frontend/pwa/installPrompt.ts'

await initI18n('en')

// Pages are lazy: the first test of a file waits for App and its page to load, which with coverage on (CI) can take
// well over the 1 s default of findBy*/waitFor. Stays below the test timeout (15 s, vite.config.ts), so a miss still names the element.
configure({ asyncUtilTimeout: 3000 })

// Motion would keep a leaving item in the DOM while it animates out: tests check the outcome, not the animation.
MotionGlobalConfig.skipAnimations = true

afterEach(async () => {
  cleanup()
  window.localStorage.clear()
  window.sessionStorage.clear()
  installPrompt.reset()
  appUpdate.reset()
  connectivity.reset()
  await i18n.changeLanguage('en')
})

// jsdom lacks a few browser APIs that MUI and MUI X (pickers, grid, charts, popovers) rely on.
Element.prototype.hasPointerCapture ??= () => false
Element.prototype.setPointerCapture ??= () => {}
Element.prototype.releasePointerCapture ??= () => {}
Element.prototype.scrollIntoView ??= () => {}
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
}
