import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { COLOR_MODE_KEY } from '../../../src/frontend/theme/colorMode.ts'
import { THEME_COLOR } from '../../../src/frontend/theme/initScheme.ts'
import { components, glass, SHADOWS, TONES } from '../../../src/frontend/theme/components.ts'
import { createTankstatTheme } from '../../../src/frontend/theme/theme.ts'
import { ThemeRoot } from '../../../src/frontend/theme/ThemeRoot.tsx'

const theme = createTankstatTheme()

describe('the theme keeps the old look', () => {
  it('has the old breakpoints under MUI names, plus xxl', () => {
    expect(theme.breakpoints.values).toEqual({ xs: 0, sm: 520, md: 768, lg: 1024, xl: 1280, xxl: 1640 })
  })

  it('counts space in 8 px units, so the old Radix steps are halves (space 3 = 1.5)', () => {
    expect(theme.vars.spacing).toBe('var(--mui-spacing, 8px)')
    expect([theme.spacing(0.5), theme.spacing(1.5)]).toEqual(['calc(0.5 * var(--mui-spacing, 8px))', 'calc(1.5 * var(--mui-spacing, 8px))'])
  })

  it('is dark by default with the same gray and accent, and has a light scheme from the same scales', () => {
    const { dark, light } = theme.colorSchemes
    expect(dark?.palette.background.default).toBe('#111110')
    expect(dark?.palette.primary.main).toBe('#3e63dd')
    expect(light?.palette.primary.main).toBe('#3e63dd')
    expect(light?.palette.text.primary).toBe('#21201c')
  })

  it('gives every tone soft surfaces and readable text in both schemes', () => {
    for (const scheme of ['dark', 'light'] as const)
      for (const tone of TONES) {
        const c = theme.colorSchemes[scheme]!.palette[tone]
        expect([c.soft, c.softHover, c.softText].every((v) => /^#[0-9a-f]{6,8}$/.test(v)), `${scheme} ${tone}`).toBe(true)
      }
  })

  it('maps the heading sizes: h6 is the old size 3 (16 px) up to h1, the old size 8 (35 px)', () => {
    expect([theme.typography.h6.fontSize, theme.typography.h4.fontSize, theme.typography.h1.fontSize]).toEqual(['1rem', '1.25rem', '2.1875rem'])
  })

  it('draws elevations with the soft shadows of the scheme', () => {
    expect([SHADOWS[0], SHADOWS[1], SHADOWS[8], SHADOWS[24]]).toEqual(['none', 'var(--tk-shadow-2)', 'var(--tk-shadow-4)', 'var(--tk-shadow-5)'])
  })
})

describe('the surfaces', () => {
  const baseline = (components.MuiCssBaseline!.styleOverrides as (t: typeof theme) => Record<string, Record<string, string>>)(theme)

  it('draw every floating panel from the same variables, solid paper when opaque', () => {
    const panel = glass(theme)
    expect(panel.backgroundColor).toMatch(/\/ var\(--tk-glass-alpha\)\)$/)
    expect([panel.backdropFilter, panel.WebkitBackdropFilter]).toEqual(['var(--tk-glass-filter)', 'var(--tk-glass-filter)'])
    // In the panel's own scheme: an always-dark card stays dark and solid on a light page.
    expect(panel['.surface-opaque &']).toEqual({ backgroundColor: theme.vars.palette.background.paper })
  })

  it('are glossy by default: see-through per scheme and blurred, the card scrim too', () => {
    expect([baseline[':root, .dark']['--tk-glass-alpha'], baseline['.light']['--tk-glass-alpha']]).toEqual(['0.7', '0.85'])
    expect(baseline[':root, .dark']['--tk-shadow-2']).toBeDefined() // merged with the shadows, not replacing them
    expect(baseline[':root']).toMatchObject({ '--tk-glass-filter': 'blur(14px) saturate(140%)', '--tk-scrim-filter': 'blur(10px)' })
  })

  it('lose the blur when transparent or opaque, and the card scrim turns darker, then solid', () => {
    expect(baseline[':root.surface-transparent']).toEqual({ '--tk-glass-filter': 'none', '--tk-scrim': 'rgba(0, 0, 0, 0.55)', '--tk-scrim-filter': 'none' })
    expect(baseline[':root.surface-opaque']).toEqual({ '--tk-glass-filter': 'none', '--tk-scrim': 'rgb(17, 17, 16)', '--tk-scrim-filter': 'none' })
  })
})

describe('the colour mode', () => {
  it('is dark when nothing was chosen', () => {
    render(<ThemeRoot instant>app</ThemeRoot>)
    expect(document.documentElement).toHaveClass('dark')
  })

  it('follows what the browser kept', () => {
    window.localStorage.setItem(COLOR_MODE_KEY, 'light')
    render(<ThemeRoot instant>app</ThemeRoot>)
    expect(document.documentElement).toHaveClass('light')
  })

  it('colours the browser bar with the scheme on screen', () => {
    document.head.insertAdjacentHTML('beforeend', '<meta name="theme-color" content="#1f2a6b" />')
    window.localStorage.setItem(COLOR_MODE_KEY, 'light')
    render(<ThemeRoot instant>app</ThemeRoot>)
    expect(document.querySelector('meta[name="theme-color"]')).toHaveAttribute('content', THEME_COLOR.light)
  })
})
