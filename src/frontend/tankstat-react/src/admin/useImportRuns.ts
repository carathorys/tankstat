import { useQuery } from '@apollo/client/react'
import { useEffect } from 'react'
import { ImportRunsDocument, type ImportRunFieldsFragment } from '../gql/generated.ts'

/** How often the administrators' lists ask while an import goes or waits. */
export const IMPORT_RUNS_POLL_MS = 2000

/**
 * The imports that go or wait on the instance (#337, the owner's decision 4), for administrators: asked when the page opens and again
 * every two seconds while there is one. Empty for anyone the server refuses.
 */
export function useImportRuns(): { runs: ImportRunFieldsFragment[]; refetch: () => void } {
  const { data, refetch, startPolling, stopPolling } = useQuery(ImportRunsDocument, { fetchPolicy: 'network-only' })
  const runs = data?.importRuns ?? []
  const some = runs.length > 0
  // Polled only while there is something to watch end: one query, so an empty answer empties the list.
  useEffect(() => {
    if (!some) return
    startPolling(IMPORT_RUNS_POLL_MS)
    return stopPolling
  }, [some, startPolling, stopPolling])
  return { runs, refetch: () => void refetch().catch(() => undefined) }
}
