import { useEffect, useState } from 'react'

/** The value, but only after it stopped changing for `delayMs` (so typing in a search box does not ask the server for every letter). */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}
