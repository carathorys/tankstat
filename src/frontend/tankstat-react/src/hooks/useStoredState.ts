import { useCallback, useState } from 'react'

/** State kept in the browser's localStorage (and nowhere else). A broken or unavailable storage just means the default. */
export function useStoredState<T>(key: string, initial: T, isValid: (value: unknown) => value is T): [T, (value: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = window.localStorage.getItem(key)
      if (raw !== null) {
        const parsed: unknown = JSON.parse(raw)
        if (isValid(parsed)) return parsed
      }
    } catch {
      // unreadable: use the default
    }
    return initial
  })

  const update = useCallback(
    (next: T) => {
      setValue(next)
      try {
        window.localStorage.setItem(key, JSON.stringify(next))
      } catch {
        // not persisting is acceptable
      }
    },
    [key],
  )

  return [value, update]
}
