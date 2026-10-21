import { renderHook } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useHoldTakeover, useTakeoverHeld } from '../../src/setup/takeoverHold.ts'

it('holds the takeover while any part of the setup page holds it, and lets go once none does', () => {
  const held = renderHook(() => useTakeoverHeld())
  const restore = renderHook(({ active }) => useHoldTakeover(active), { initialProps: { active: false } })
  const finish = renderHook(({ active }) => useHoldTakeover(active), { initialProps: { active: true } })
  expect(held.result.current).toBe(true)

  restore.rerender({ active: true })
  finish.rerender({ active: false }) // the last step's wait is over; the restore's goes on
  expect(held.result.current).toBe(true)

  restore.unmount() // the page went (the setup was done, the page reloaded)
  expect(held.result.current).toBe(false)
  finish.unmount()
  held.unmount()
})
