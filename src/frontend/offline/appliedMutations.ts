/**
 * The mutations the server applied in this tab, by name (the offline link tells), for what must follow them up: the runtime downloads the
 * offline window again soon after a vehicle came in (added or restored), so it can be used offline straight away.
 */
const listeners = new Set<(operation: string) => void>()

export const appliedMutations = {
  tell(operation: string): void {
    listeners.forEach((listener) => listener(operation))
  },

  subscribe(listener: (operation: string) => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
}
