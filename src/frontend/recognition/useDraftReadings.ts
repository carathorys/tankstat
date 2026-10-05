import { useQuery } from '@apollo/client/react'
import { useEffect, useState } from 'react'
import { PhotoDraftReadingsDocument, RecognitionStatusDocument, type PhotoDraftReadingsQuery } from '../gql/generated.ts'
import { mergeIssues, type ReadingExplanation } from './readingIssues.ts'

/** How often the open dialog asks for readings that are not done yet. */
export const READING_POLL_MS = 1500
/** How long the dialog waits for one photo; the server keeps trying, but the user is not kept waiting longer. */
export const READING_WAIT_MS = 60_000
/** How often the dialog asks again whether photo reading is on while it reads "off" and a photo was just uploaded (the provider may be back). */
export const STATUS_RECHECK_MS = 3000

export type DraftReading = NonNullable<PhotoDraftReadingsQuery['photoDrafts'][number]['reading']>

export interface DraftReadings {
  /** Photo reading is on (the server reads photos right now). */
  available: boolean
  /** The reading of each uploaded draft (null: the draft is not being read). */
  readings: ReadonlyMap<string, DraftReading | null>
  /** Drafts still being read, which the dialog waits for. */
  pending: string[]
  /** Every reading the dialog waited for has finished (at least one). */
  done: boolean
  /** Why the finished readings gave less than they might have, for the dialog to say. */
  explanation: ReadingExplanation
}

/**
 * The readings of the drafts uploaded in an open add dialog. Asks whether photo reading is on when the dialog opens and, if so, polls the
 * drafts' readings while any is still being read (for at most a minute after each upload). While it reads "off", it asks again for a minute
 * after each upload: the server queues the photo anyway and reads it once a briefly unreachable provider is back. Nothing is asked while
 * `enabled` is false.
 */
export function useDraftReadings(drafts: readonly { id: string; at: number }[], enabled: boolean): DraftReadings {
  const status = useQuery(RecognitionStatusDocument, { skip: !enabled, fetchPolicy: 'network-only' })
  const available = enabled && status.data?.recognitionStatus.available === true
  const [now, setNow] = useState(() => Date.now())
  const ids = drafts.map((d) => d.id)

  const result = useQuery(PhotoDraftReadingsDocument, { variables: { ids }, skip: !available || ids.length === 0, fetchPolicy: 'network-only' })
  const readings = new Map<string, DraftReading | null>((result.data?.photoDrafts ?? []).map((d) => [d.id, d.reading ?? null]))
  const pending = available
    ? drafts
        .filter(({ id, at }) => {
          const reading = readings.get(id)
          const waiting = reading === undefined || (reading !== null && (reading.status === 'QUEUED' || reading.status === 'READING'))
          return waiting && now - at < READING_WAIT_MS
        })
        .map((d) => d.id)
    : []
  const polling = pending.length > 0
  const rechecking = enabled && status.data?.recognitionStatus.available === false && drafts.some(({ at }) => now - at < READING_WAIT_MS)
  const { startPolling, stopPolling } = result
  const { startPolling: startStatusPolling, stopPolling: stopStatusPolling } = status

  useEffect(() => {
    if (!polling) return undefined
    startPolling(READING_POLL_MS)
    return stopPolling
  }, [polling, startPolling, stopPolling])

  useEffect(() => {
    if (!rechecking) return undefined
    startStatusPolling(STATUS_RECHECK_MS)
    return stopStatusPolling
  }, [rechecking, startStatusPolling, stopStatusPolling])

  const waiting = polling || rechecking
  useEffect(() => {
    if (!waiting) return undefined
    // Polls that bring nothing new do not render, so the clock is moved on to let a photo that takes too long drop out.
    const clock = setInterval(() => setNow(Date.now()), 5000)
    return () => clearInterval(clock)
  }, [waiting])

  const finished = ids.some((id) => {
    const reading = readings.get(id)
    return reading?.status === 'READ' || reading?.status === 'FAILED'
  })
  const failed = ids.some((id) => readings.get(id)?.status === 'FAILED')
  return { available, readings, pending, done: available && !polling && finished, explanation: { issues: mergeIssues(readings.values()), failed } }
}
