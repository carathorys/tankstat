import { COLOR_MODE_KEY } from './colorMode.ts'
import { SAND, step } from './scales.ts'

// Loaded by vite.config.ts in Node as well: keep it free of anything but these two plain modules.

/** The browser's bar (meta theme-color) in each scheme: the brand indigo when dark, its light counterpart when light. */
export const THEME_COLOR = { dark: '#1f2a6b', light: '#e1e9ff' } as const

/**
 * Runs in index.html before anything paints (vite.config.ts puts it there): the scheme the user chose (the browser's copy, which MUI keeps
 * under COLOR_MODE_KEY: light, dark or system; dark when there is none) goes on <html> as MUI's class, and the browser's bar follows it.
 * MUI takes over once the app runs and keeps both up to date.
 */
export function initSchemeScript(): string {
  const key = JSON.stringify(COLOR_MODE_KEY)
  return (
    `(function(){try{var m=localStorage.getItem(${key});` +
    `var s=m==='light'||(m==='system'&&!window.matchMedia('(prefers-color-scheme: dark)').matches)?'light':'dark';` +
    `var h=document.documentElement;h.classList.remove('light','dark');h.classList.add(s);` +
    `var t=document.querySelector('meta[name="theme-color"]');` +
    `if(t)t.setAttribute('content',s==='light'?${JSON.stringify(THEME_COLOR.light)}:${JSON.stringify(THEME_COLOR.dark)})` +
    `}catch(e){}})();`
  )
}

/**
 * The page's colour from the first paint, until the app's own styles arrive with its script (they come with the JavaScript, not as a
 * stylesheet). Weightless selectors, so the theme's rules win as soon as they are there.
 */
export function initSchemeStyle(): string {
  return (
    `:where(html){color-scheme:dark}:where(html.light){color-scheme:light}` +
    `:where(body){margin:0;background-color:${step(SAND.dark, 1)}}:where(html.light body){background-color:${step(SAND.light, 3)}}`
  )
}
