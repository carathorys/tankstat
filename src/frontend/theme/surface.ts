// Loaded by vite.config.ts in Node as well (through initScheme.ts): no imports and nothing of the page (the store is surfaceStore.ts).

/** Where the browser keeps the surface style; the account keeps it too (UiSettings). */
export const SURFACE_KEY = 'tankstat.surface'

/**
 * How the floating surfaces (top bar, navigation, dialogs, menus, popovers, cards, tables, messages) are drawn: see-through and blurred
 * (glossy, the default), see-through only (transparent) or solid (opaque). The server's enum in capitals: GLOSSY, TRANSPARENT, OPAQUE.
 */
export const SURFACES = ['glossy', 'transparent', 'opaque'] as const
export type SurfaceChoice = (typeof SURFACES)[number]

/** The class on <html> that switches the surfaces' CSS variables (theme/components.ts); glossy has none. */
export const SURFACE_CLASS: Record<SurfaceChoice, string | null> = { glossy: null, transparent: 'surface-transparent', opaque: 'surface-opaque' }
