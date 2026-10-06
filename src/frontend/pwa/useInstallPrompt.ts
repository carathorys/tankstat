import { useSyncExternalStore } from 'react'
import { installPrompt } from './installPrompt.ts'
import { platform } from './platform.ts'

/**
 * Whether the app can be installed from here: Chromium browsers offer it (`canInstall`, then `install()` shows their dialog); on an
 * iPhone or iPad the user does it from Safari's Share menu (`showIosHint` asks for the how-to). Nothing once the app runs installed.
 */
export function useInstallPrompt(): { canInstall: boolean; install: () => Promise<boolean>; showIosHint: boolean } {
  const available = useSyncExternalStore(installPrompt.subscribe, () => installPrompt.available, () => false)
  const standalone = platform.isStandalone()
  return { canInstall: available && !standalone, install: installPrompt.install, showIosHint: !available && !standalone && platform.isIos() }
}
