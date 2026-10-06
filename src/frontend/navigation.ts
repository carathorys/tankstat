/**
 * Full-page navigations that leave the app (the sign-in redirect). One object, so tests replace it: jsdom cannot navigate and does not
 * let `window.location` be spied on.
 */
export const navigation = {
  replace: (url: string): void => window.location.replace(url),
}
