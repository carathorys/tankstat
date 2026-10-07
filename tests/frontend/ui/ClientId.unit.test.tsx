import { renderHook } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useClientId } from '../../../src/frontend/dialogs/useClientId.ts'

it('an add dialog keeps one id while it is open and gets a new one each time it opens', () => {
  const { result, rerender } = renderHook(({ open }) => useClientId(open), { initialProps: { open: true } })
  const first = result.current

  rerender({ open: true })
  expect(result.current).toBe(first)
  rerender({ open: false })
  expect(result.current).toBe(first) // closing changes nothing yet: the dialog may still be fading out
  rerender({ open: true })
  expect(result.current).not.toBe(first)
})
