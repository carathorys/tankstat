import { useSyncExternalStore } from 'react'
import { SURFACE_CLASS, SURFACE_KEY, SURFACES, type SurfaceChoice } from './surface.ts'

const listeners = new Set<() => void>()

/** The surface style in effect: the class on <html> (the pre-paint script put the browser's copy there) or glossy. */
export function currentSurface(): SurfaceChoice {
  const classes = document.documentElement.classList
  return SURFACES.find((s) => SURFACE_CLASS[s] !== null && classes.contains(SURFACE_CLASS[s]!)) ?? 'glossy'
}

/** Switches the surfaces: the class on <html> at once, and the browser's copy (a storage failure only loses the copy). */
export function setSurface(choice: SurfaceChoice) {
  const classes = document.documentElement.classList
  for (const s of SURFACES) if (SURFACE_CLASS[s]) classes.toggle(SURFACE_CLASS[s]!, s === choice)
  try {
    localStorage.setItem(SURFACE_KEY, choice)
  } catch {
    // private mode or storage blocked: the class still applies for this page
  }
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** The surface style in effect, re-rendering when it is switched. */
export function useSurface(): SurfaceChoice {
  return useSyncExternalStore(subscribe, currentSurface, () => 'glossy')
}
