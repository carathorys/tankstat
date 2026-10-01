import { useEffect, useState } from 'react'

/** Follows a CSS media query. Where the browser cannot answer (no matchMedia), `fallback` is used. */
export function useMediaQuery(query: string, fallback: boolean): boolean {
  const supported = typeof window !== 'undefined' && typeof window.matchMedia === 'function'
  const [matches, setMatches] = useState(() => (supported ? window.matchMedia(query).matches : fallback))

  useEffect(() => {
    if (!supported) return
    const list = window.matchMedia(query)
    const onChange = () => setMatches(list.matches)
    onChange()
    list.addEventListener('change', onChange)
    return () => list.removeEventListener('change', onChange)
  }, [query, supported])

  return matches
}
