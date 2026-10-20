import type { CSSProperties } from 'react'

/** Out of sight but still read by screen readers (a table caption, a live status), or kept for the form without being shown. */
export const visuallyHidden: CSSProperties = {
  border: 0,
  clip: 'rect(0 0 0 0)',
  height: 1,
  margin: -1,
  overflow: 'hidden',
  padding: 0,
  position: 'absolute',
  whiteSpace: 'nowrap',
  width: 1,
}
