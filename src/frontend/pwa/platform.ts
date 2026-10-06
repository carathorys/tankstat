/** What kind of browser this is, as far as installing the app goes. An object, so a test can replace one answer. */
export const platform = {
  /** Running as an installed app (home screen, dock), not in a browser tab. */
  isStandalone: (): boolean =>
    (typeof window.matchMedia === 'function' && window.matchMedia('(display-mode: standalone)').matches) ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true,
  /** iPhone or iPad: Safari makes no install offer, the user adds the app from the Share menu. iPadOS presents itself as a Mac with touch. */
  isIos: (): boolean => /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1),
}
