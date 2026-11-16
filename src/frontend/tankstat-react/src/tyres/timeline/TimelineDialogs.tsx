import { useApolloClient } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { OfflineNote } from '../../components/OfflineNote.tsx'
import { LabeledSelect } from '../../components/UnitSelect.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../../dialogs/DialogFrame.tsx'
import { useClientId } from '../../dialogs/useClientId.ts'
import { Field } from '../../forms/Field.tsx'
import { FieldDate } from '../../forms/FieldDate.tsx'
import { Form } from '../../forms/Form.tsx'
import { AddTyreInstallationDocument, DeleteTyreInstallationDocument, RestoreTyreInstallationDocument, UpdateTyreInstallationDocument } from '../../gql/generated.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage } from '../../messages.tsx'
import { discardChange } from '../../offline/submitChange.ts'
import { type Tyres, useTyreMutation, useTyreToasts } from '../tyres.ts'
import { isoOf, type Boundary, type Gap } from './model.ts'
import { useMoveSwap } from './useMoveSwap.ts'

const setName = (tyres: Tyres, id: string) => tyres.sets.find((s) => s.set.id === id)?.set.name ?? ''

/** *Move the swap*: one day for both sets. Opened by the timeline (a handle, or the table's button). */
export function MoveSwapDialog({ vehicleId, tyres, boundary, open, onOpenChange, onClosed }: { vehicleId: string; tyres: Tyres; boundary: Boundary; open: boolean; onOpenChange: (open: boolean) => void; onClosed: () => void }) {
  const { t } = useTranslation()
  const move = useMoveSwap(vehicleId)
  const [day, setDay] = useState(isoOf(boundary.day))
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      if (day !== isoOf(boundary.day)) await move(boundary, day)
      onOpenChange(false)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }
  return (
    <DialogFrame
      open={open}
      onClose={() => onOpenChange(false)}
      onClosed={onClosed}
      busy={busy}
      maxWidth={440}
      title={t('tyres.timeline.moveTitle')}
      description={t('tyres.timeline.moveText', { off: setName(tyres, boundary.before.setId), on: setName(tyres, boundary.after.setId) })}
    >
      <Form onSubmit={submit} refusal={error}>
        <OfflineNote />
        <Stack sx={{ gap: 1.5 }}>
          <Field name="day" label={t('tyres.timeline.moveDay')} hint={t('tyres.timeline.moveHint')} required>
            <FieldDate disableFuture value={day} onChange={setDay} />
          </Field>
          {error !== undefined && <ErrorMessage error={error} />}
          <DialogButtons>
            <DialogCancel />
            <Button type="submit" disabled={busy}>
              {t('tyres.dialogs.save')}
            </Button>
          </DialogButtons>
        </Stack>
      </Form>
    </DialogFrame>
  )
}

/**
 * *Which set was on the car?* (#332, edit C): a gap filled with a set. `addTyreInstallation` never joins, so the set next to the gap
 * stretches its own period when the chosen days touch it: the set before, from the gap's first day, ends later; the set after, up to the
 * gap's last day, starts earlier (its line, reading and expense move with it); anything else adds a period. The same set on both sides,
 * filled whole: the server never joins two periods of a set that meet, so the later one goes to the trash with its line and the earlier
 * one runs to where it ended, one period. When the later one's line holds something (a reading, a note, an amount), or the set is tracked
 * tyre by tyre (its tyres' corners in that period would go too), nothing is trashed: the earlier one stretches to it and the two meet.
 */
export function FillGapDialog({ vehicleId, tyres, gap, open, onOpenChange, onClosed }: { vehicleId: string; tyres: Tyres; gap: Gap; open: boolean; onOpenChange: (open: boolean) => void; onClosed: () => void }) {
  const { t } = useTranslation()
  const format = useFormat()
  const run = useTyreMutation(vehicleId)
  const notify = useTyreToasts()
  const client = useApolloClient()
  const id = useClientId(open)
  const sets = tyres.sets.filter((s) => !s.set.isRetired)
  // The set before the gap first: the likeliest answer is that it simply stayed on.
  const [setId, setSetId] = useState(sets.some((s) => s.set.id === gap.before.setId) ? gap.before.setId : (sets[0]?.set.id ?? ''))
  const [from, setFrom] = useState(isoOf(gap.from))
  const [until, setUntil] = useState(isoOf(gap.until))
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)
  const stretchesAfter = setId === gap.after.setId && until === isoOf(gap.until) && !(setId === gap.before.setId && from === isoOf(gap.from))

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    const whole = (p: Gap['before'], days: { from: string; until: string | null }) => ({ id: p.id, ...days, pairOneFront: p.info.installation.pairOneFront ?? null, odometerFrom: null, odometerUntil: null })
    const before = gap.before.info.installation
    const after = gap.after.info.installation
    try {
      let saved
      if (setId === gap.before.setId && from === isoOf(gap.from)) {
        const line = tyres.entries.find((x) => x.installationId === after.id)
        // What trashing the later period would take along: something typed on its line, or (a set tracked tyre by tyre) its tyres' corners.
        const holds = !!line && (line.odometer != null || !!line.note || line.expenseId != null)
        const tracked = !!tyres.sets.find((x) => x.set.id === setId)?.set.brokenUpOn
        if (setId === gap.after.setId && until === isoOf(gap.until) && !holds && !tracked) {
          // One set all along: one period. The later one's empty line goes to the trash with it (the trash brings both back).
          const trashed = await run(DeleteTyreInstallationDocument, { id: after.id })
          try {
            saved = await run(UpdateTyreInstallationDocument, { input: whole(gap.before, { from: before.from, until: after.until ?? null }) })
          } catch (err) {
            // The stretch was refused: the later period comes back (a trash kept on this device is taken back), so nothing is left half done.
            await (trashed.queued ? discardChange(client, trashed.changeId) : run(RestoreTyreInstallationDocument, { id: after.id })).catch(() => undefined)
            throw err
          }
        } else saved = await run(UpdateTyreInstallationDocument, { input: whole(gap.before, { from: before.from, until }) })
      } else if (stretchesAfter) saved = await run(UpdateTyreInstallationDocument, { input: whole(gap.after, { from, until: after.until ?? null }) })
      else saved = await run(AddTyreInstallationDocument, { input: { id, setId, from, until, pairOneFront: null, odometerFrom: null, odometerUntil: null } })
      onOpenChange(false)
      notify.saved(saved, t('toast.saved'))
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }
  return (
    <DialogFrame
      open={open}
      onClose={() => onOpenChange(false)}
      onClosed={onClosed}
      busy={busy}
      maxWidth={440}
      title={t('tyres.timeline.gapTitle')}
      description={t('tyres.timeline.gapNothing', { from: format.date(isoOf(gap.from)), until: format.date(isoOf(gap.until)) })}
    >
      <Form onSubmit={submit} refusal={error}>
        <OfflineNote />
        <Stack sx={{ gap: 1.5 }}>
          <LabeledSelect
            label={t('tyres.timeline.set')}
            value={setId}
            options={sets.map((s) => ({ value: s.set.id, label: `${t(`tyres.seasons.${s.set.season}`)}: ${s.set.name}` }))}
            onChange={setSetId}
          />
          <Field name="from" label={t('tyres.fields.from')} hint={stretchesAfter ? t('tyres.timeline.moveHint') : undefined} required>
            <FieldDate disableFuture value={from} onChange={setFrom} />
          </Field>
          <Field name="until" label={t('tyres.fields.until')} required>
            <FieldDate disableFuture value={until} onChange={setUntil} />
          </Field>
          {error !== undefined && <ErrorMessage error={error} />}
          <DialogButtons>
            <DialogCancel />
            <Button type="submit" disabled={busy || setId === ''}>
              {t('tyres.dialogs.save')}
            </Button>
          </DialogButtons>
        </Stack>
      </Form>
    </DialogFrame>
  )
}
