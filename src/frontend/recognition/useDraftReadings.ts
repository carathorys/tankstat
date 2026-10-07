import { useQuery } from '@apollo/client/react'
import { useEffect, useState } from 'react'
import { ExpensePhotoReadingsDocument, PhotoDraftReadingsDocument, RecognitionStatusDocument, RefuelingPhotoReadingsDocument, type PhotoReadingFieldsFragment } from '../gql/generated.ts'
import type { LogKind } from '../pictures/upload.ts'
import { mergeIssues, type ReadingExplanation } from './readingIssues.ts'

/** How often the open dialog asks for readings that are not done yet. */
export const READING_POLL_MS = 1500
/**
 * How long the dialog waits for one photo: a little under the server's `LogPhotoFiller.JustRead` (2 minutes from the upload), so while the
 * dialog still says values may be left to the photo, the server accepts that. The server keeps trying afterwards and fills the saved log.
 */
export const READING_WAIT_MS = 110_000
/** How often the dialog asks again whether photo reading is on while it reads "off" and a photo was just uploaded (the provider may be back). */
export const STATUS_RECHECK_MS = 3000

export type DraftReading = PhotoReadingFieldsFragment

export interface DraftReadings {
  /** Photo reading is on (the server reads photos right now). */
  available: boolean
  /** The server said photo reading is off (unknown until it answers). */
  off: boolean
  /** The reading of each uploaded draft (null: the draft is not being read). */
  readings: ReadonlyMap<string, DraftReading | null>
  /** Drafts still being read, which the dialog waits for. */
  pending: string[]
  /** Every reading the dialog waited for has finished (at least one). */
  done: boolean
  /** Why the finished readings gave less than they might have, for the dialog to say. */
  explanation: ReadingExplanation
  /** The window the dialog waits in for the pending readings: from the first pending upload to READING_WAIT_MS after the last one. */
  waitingSince: number | null
  waitingUntil: number | null
}

/**
 * The readings of the photos uploaded in an open dialog: the drafts of an add dialog, or, with `log`, the photos added to that saved log in
 * its edit dialog. Asks whether photo reading is on when the dialog opens and, if so, polls the readings while any is still being read (for
 * at most a minute after each upload). While it reads "off", it asks again for a minute after each upload: the server queues the photo
 * anyway and reads it once a briefly unreachable provider is back. Nothing is asked while `enabled` is false.
 *
 * A log's readings are asked without Apollo's cache: the answer holds the log's photos without their urls, which would otherwise leave the
 * dialog's own query of the log incomplete.
 */
export function useDraftReadings(drafts: readonly { id: string; at: number }[], enabled: boolean, log?: { kind: LogKind; id: string }): DraftReadings {
  const status = useQuery(RecognitionStatusDocument, { skip: !enabled, fetchPolicy: 'network-only' })
  const available = enabled && status.data?.recognitionStatus.available === true
  const [now, setNow] = useState(() => Date.now())
  const ids = drafts.map((d) => d.id)
  const asking = available && ids.length > 0

  const ofDrafts = useQuery(PhotoDraftReadingsDocument, { variables: { ids }, skip: !asking || log !== undefined, fetchPolicy: 'network-only' })
  const ofRefueling = useQuery(RefuelingPhotoReadingsDocument, { variables: { id: log?.id ?? '' }, skip: !asking || log?.kind !== 'refuelings', fetchPolicy: 'no-cache' })
  const ofExpense = useQuery(ExpensePhotoReadingsDocument, { variables: { id: log?.id ?? '' }, skip: !asking || log?.kind !== 'expenses', fetchPolicy: 'no-cache' })
  const result = log === undefined ? ofDrafts : log.kind === 'refuelings' ? ofRefueling : ofExpense
  const found = log === undefined ? (ofDrafts.data?.photoDrafts ?? []) : ((log.kind === 'refuelings' ? ofRefueling.data?.refueling?.photos : ofExpense.data?.expense?.photos) ?? [])
  const readings = new Map<string, DraftReading | null>(found.filter((p) => ids.includes(p.id)).map((p) => [p.id, p.reading ?? null]))
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
  const off = enabled && status.data?.recognitionStatus.available === false
  const pendingSince = drafts.filter((d) => pending.includes(d.id)).map((d) => d.at)
  return {
    available,
    off,
    readings,
    pending,
    done: available && !polling && finished,
    explanation: { issues: mergeIssues(readings.values()), failed },
    waitingSince: pendingSince.length > 0 ? Math.min(...pendingSince) : null,
    waitingUntil: pendingSince.length > 0 ? Math.max(...pendingSince) + READING_WAIT_MS : null,
  }
}
