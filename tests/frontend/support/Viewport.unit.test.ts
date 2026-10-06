import { expect, it } from 'vitest'
import { matchesMedia } from './mocks.tsx'

// stubViewport answers real media queries, so a phone test never turns into a desktop one because a query is spelt differently.
it('evaluates widths, with or without spaces and @media', () => {
  expect(matchesMedia('(max-width: 767px)', 'phone')).toBe(true)
  expect(matchesMedia('@media (min-width:768px)', 'phone')).toBe(false)
  expect(matchesMedia('(min-width:1024px)', 'desktop')).toBe(true)
  expect(matchesMedia('(max-width: 767px)', 'desktop')).toBe(false)
})

it('knows a phone has touch and a desktop a mouse', () => {
  expect(matchesMedia('(hover: none)', 'phone')).toBe(true)
  expect(matchesMedia('(pointer: fine)', 'phone')).toBe(false)
  expect(matchesMedia('(pointer: fine)', 'desktop')).toBe(true)
  expect(matchesMedia('(hover: hover) and (pointer: fine)', 'desktop')).toBe(true)
})

it('joins with and, offers alternatives with commas, and does not match what it does not know', () => {
  expect(matchesMedia('(min-width: 300px) and (max-width: 500px)', 'phone')).toBe(true)
  expect(matchesMedia('(min-width: 2000px), (hover: none)', 'phone')).toBe(true)
  expect(matchesMedia('(orientation: landscape)', 'desktop')).toBe(false)
  expect(matchesMedia('(prefers-color-scheme: dark)', 'desktop')).toBe(true)
})

it('has a dark colour scheme unless the test sets the device to light', () => {
  expect(matchesMedia('(prefers-color-scheme: light)', 'phone')).toBe(false)
  expect(matchesMedia('(prefers-color-scheme: light)', 'phone', 'light')).toBe(true)
  expect(matchesMedia('(prefers-color-scheme: dark)', 'desktop', 'light')).toBe(false)
})
