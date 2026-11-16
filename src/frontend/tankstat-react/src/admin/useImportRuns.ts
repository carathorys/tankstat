import { useQuery } from '@apollo/client/react'
import { ImportRunsDocument, type ImportRunFieldsFragment } from '../gql/generated.ts'

/** How often the administrators' lists ask while an import goes or waits. */
export const IMPORT_RUNS_POLL_MS = 2000

/**
 * The imports that go or wait on the instance (#337, the owner's decision 4), for administrators: asked when the page opens and again
 * every two seconds while there is one. Empty for anyone the server refuses.
 */
export function useImportRuns(): { runs: ImportRunFieldsFragment[]; refetch: () => void } {
  const query = useQuery(ImportRunsDocument, { fetchPolicy: 'network-only' })
  const runs = query.data?.importRuns ?? []
  const some = runs.length > 0
  // Polled only while there is something to watch end.
  const polled = useQuery(ImportRunsDocument, { fetchPolicy: 'network-only', pollInterval: IMPORT_RUNS_POLL_MS, skip: !some })
  return { runs: polled.data?.importRuns ?? runs, refetch: () => void query.refetch().catch(() => undefined) }
}
