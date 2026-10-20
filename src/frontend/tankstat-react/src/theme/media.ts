/** Media queries the UI decides on in JavaScript (`useMediaQuery`); styles use the theme's breakpoints instead. */
export const MEDIA = {
  /** The docked navigation and the desktop layout. */
  desktop: '(min-width: 1024px)',
  /** Phones in portrait: the floating add button. */
  narrow: '(max-width: 767px)',
  /** A touch screen: tap to reveal what a mouse sees on hover. */
  touch: '(hover: none)',
} as const
