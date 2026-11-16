import { useTranslation } from 'react-i18next'
import { UpdateTyreEntryDocument } from '../../gql/generated.ts'
import { useFormat } from '../../i18n/format.ts'
import { useTyreMutation, useTyreToasts } from '../tyres.ts'
import type { Boundary } from './model.ts'

/**
 * Moves a swap to another day (#332, edit A): the line of the period that starts there, sent with its kind, set, reading and note as they
 * are and the new day. The server moves both periods (`TyrePlaces.MoveSwap`), the line's reading and its expense; an amount left out stays.
 * The toast's Undo sends the old day back (a change kept on the device is taken back instead).
 */
export function useMoveSwap(vehicleId: string) {
  const { t } = useTranslation()
  const format = useFormat()
  const run = useTyreMutation(vehicleId)
  const notify = useTyreToasts()
  return async (boundary: Boundary, day: string) => {
    const { line } = boundary
    const input = (to: string) => ({ id: line.id, kind: line.kind, setId: line.setId ?? null, day: to, odometer: line.odometer ?? null, note: line.note ?? null })
    const saved = await run(UpdateTyreEntryDocument, { input: input(day) })
    notify.undoable(saved, t('tyres.timeline.moved', { date: format.date(day) }), () => run(UpdateTyreEntryDocument, { input: input(line.day) }))
  }
}

