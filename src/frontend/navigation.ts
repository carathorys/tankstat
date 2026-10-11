/**
 * Full-page navigations (the sign-in redirect, a download, the reload after a restore). One object, so tests replace it: jsdom cannot
 * navigate and does not let `window.location` be spied on.
 */
export const navigation = {
  replace: (url: string): void => window.location.replace(url),
  /** A file the server sends as an attachment: the browser saves it and the page stays. */
  download: (url: string): void => window.location.assign(url),
  /** The same page afresh (after the server restarted: the session may have ended, the settings have changed). */
  reload: (): void => window.location.reload(),
}
