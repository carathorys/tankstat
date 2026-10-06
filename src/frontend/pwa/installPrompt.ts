/** Chromium's `beforeinstallprompt` event: the browser's offer to install the app, which the page may show later with `prompt()`. */
export interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>
}

let offer: BeforeInstallPromptEvent | null = null
const listeners = new Set<() => void>()
const notify = () => listeners.forEach((listener) => listener())

/**
 * The browser's offer to install the app, kept from the moment it is made until it is used or the app is installed. Chromium makes the
 * offer once, early (long before any menu exists) and shows its own banner unless the page takes it over, hence `preventDefault` and a
 * module that main.tsx imports first of all. Components read it through `useInstallPrompt`.
 */
export const installPrompt = {
  /** Whether the browser has offered to install and the offer has not been used yet. */
  get available(): boolean {
    return offer !== null
  },
  /** Shows the browser's install dialog; true when the user accepted. The offer is spent either way. */
  async install(): Promise<boolean> {
    const current = offer
    if (!current) return false
    offer = null
    notify()
    await current.prompt()
    const { outcome } = await current.userChoice
    return outcome === 'accepted'
  },
  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  /** Tests: forget a kept offer. */
  reset(): void {
    offer = null
    notify()
  },
}

if (typeof window !== 'undefined') {
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault()
    offer = e as BeforeInstallPromptEvent
    notify()
  })
  window.addEventListener('appinstalled', () => {
    offer = null
    notify()
  })
}
