import { useCallback, useState } from 'react'

/**
 * Rows on their way out of a grid (`ServerGrid`'s `leaving`): `leave(id, work)` marks the row while `work` runs (the change that removes
 * it, with the grid's refetch), so it fades rather than vanishing; the mark goes once the work is done, whether it worked or not.
 */
export function useLeavingRows() {
  const [leaving, setLeaving] = useState<ReadonlySet<string>>(() => new Set())
  const leave = useCallback(async <T,>(id: string, work: () => Promise<T>): Promise<T> => {
    setLeaving((current) => new Set(current).add(id))
    try {
      return await work()
    } finally {
      setLeaving((current) => {
        const next = new Set(current)
        next.delete(id)
        return next
      })
    }
  }, [])
  return { leaving, leave }
}
