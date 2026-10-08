import { useQuery } from '@apollo/client/react'
import { useEffect } from 'react'
import { ParkedChangesDocument } from '../gql/generated.ts'
import { useConnectivity } from './useConnectivity.ts'
import { usePushState } from './usePushState.ts'

/**
 * The changes the server parked that the user may see (theirs and those of the vehicles they may see), asked while the server can be
 * reached and again after every sync of this device. The top bar, the navigation and Waiting to sync share the one answer; a list's
 * sync column (`cached`) takes it as it is rather than asking again each time a list opens (the refetches after a sync are one request:
 * Apollo sends identical queries in flight once).
 */
export function useParkedChanges(enabled = true, cached = false) {
  const { reachable } = useConnectivity()
  const last = usePushState().state.last?.at
  const query = useQuery(ParkedChangesDocument, { skip: !enabled || !reachable, fetchPolicy: cached ? 'cache-first' : 'cache-and-network' })
  const { refetch } = query
  useEffect(() => {
    if (last && enabled && reachable) void refetch().catch(() => undefined) // a failed refresh keeps the last answer
  }, [last, enabled, reachable, refetch])
  return { parked: enabled && reachable ? (query.data?.parkedChanges ?? null) : null, loading: query.loading }
}
