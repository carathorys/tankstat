import { useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Radio from '@mui/material/Radio'
import RadioGroup from '@mui/material/RadioGroup'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import useMediaQuery from '@mui/material/useMediaQuery'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Loading } from '../../components/Loading.tsx'
import { Segmented } from '../../components/Segmented.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../../dialogs/DialogFrame.tsx'
import { Field } from '../../forms/Field.tsx'
import { FieldInput } from '../../forms/FieldInput.tsx'
import { BackupImportEntriesDocument, BackupImportReviewDocument, type BackupImportEntriesQuery } from '../../gql/generated.ts'
import { useFormat } from '../../i18n/format.ts'
import { validLocale } from '../../i18n/locale.ts'
import { ErrorMessage } from '../../messages.tsx'
import { MEDIA } from '../../theme/media.ts'
import { KINDS, tyresInput, useHeldBack, useKindLine, type CarReview, type Kind, type KindChoice, type KindReview, type Kinds, type MediaChoice, type TyreChoice } from './backupKinds.ts'
import { useKeyText } from '../../i18n/errors.ts'

/**
 * *Choose what to import* under a ticked car (#338): closed at first. For a car added to one of the person's own, a block per kind with
 * what the backup has against it and the four choices; for a new car, a box per kind (each comes whole, or not at all).
 */
export function KindsDisclosure({
  token,
  car,
  target,
  kinds,
  onKinds,
  tyres,
  onTyres,
  media,
  onMedia,
}: {
  token: string
  car: { id: string; name: string; refuelings: number; expenses: number; schedules: number }
  /** The own car it is added to; null for a new car. */
  target: { id: string; name: string } | null
  kinds: Kinds
  onKinds: (kinds: Kinds) => void
  tyres: TyreChoice
  onTyres: (tyres: TyreChoice) => void
  media: MediaChoice
  onMedia: (media: MediaChoice) => void
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const panel = useId()
  const untouched = KINDS.every((kind) => kinds[kind].mode === 'ONLY_MISSING')
  return (
    <Stack sx={{ gap: 1, alignItems: 'flex-start' }}>
      <Button variant="soft" color="neutral" aria-expanded={open} aria-controls={panel} onClick={() => setOpen(!open)}>
        {open ? <ChevronDown size={16} aria-hidden /> : <ChevronRight size={16} aria-hidden />}
        {t('import.kinds.open')}
      </Button>
      {!open && target && untouched && (
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('import.kinds.closedHint')}
        </Typography>
      )}
      {/* `hidden` alone does not take a Stack out of the layout (its own `display: flex` wins over the attribute's). */}
      <Stack id={panel} hidden={!open} sx={{ gap: 2, alignSelf: 'stretch', '&[hidden]': { display: 'none' } }}>
        {open &&
          (target ? (
            <TargetKinds token={token} car={car} target={target} kinds={kinds} onKinds={onKinds} tyres={tyres} onTyres={onTyres} media={media} onMedia={onMedia} />
          ) : (
            <>
              <Typography variant="body2">{t('import.kinds.newCar')}</Typography>
              <Stack>
                {KINDS.map((kind) => (
                  <FormControlLabel
                    key={kind}
                    control={
                      <Checkbox
                        checked={kinds[kind].mode !== 'DONT_IMPORT'}
                        onChange={(e) => onKinds({ ...kinds, [kind]: { ...kinds[kind], mode: e.target.checked ? 'ONLY_MISSING' : 'DONT_IMPORT' } })}
                      />
                    }
                    label={t('import.kinds.newCarKind', {
                      name: t(`import.kinds.${kind}.name`),
                      count: kind === 'refueling' ? car.refuelings : kind === 'expense' ? car.expenses : car.schedules,
                    })}
                  />
                ))}
              </Stack>
              <NewCarTyres token={token} car={car} tyres={tyres} onTyres={onTyres} media={media} onMedia={onMedia} />
            </>
          ))}
      </Stack>
    </Stack>
  )
}

function TargetKinds({
  token,
  car,
  target,
  kinds,
  onKinds,
  tyres,
  onTyres,
  media,
  onMedia,
}: {
  token: string
  car: { id: string; name: string }
  target: { id: string; name: string }
  kinds: Kinds
  onKinds: (kinds: Kinds) => void
  tyres: TyreChoice
  onTyres: (tyres: TyreChoice) => void
  media: MediaChoice
  onMedia: (media: MediaChoice) => void
}) {
  const { t } = useTranslation()
  const heldBack = useHeldBack()
  const line = useKindLine()
  const [choosing, setChoosing] = useState<Kind | null>(null)
  // The comparison itself, as it is whatever is chosen: the choices are worked out from it here. The tyre choice goes with it, as the
  // expenses the chosen sets' lines hold come with the tyres and are not counted among the expenses (#339); asked again, the rows stay.
  const compared = useQuery(BackupImportReviewDocument, {
    variables: { token, cars: [{ carId: car.id, targetVehicleId: target.id, tyres: tyresInput(tyres) }] },
    fetchPolicy: 'network-only',
  })
  if (compared.error) return <ErrorMessage error={compared.error} />
  const review = (compared.data ?? compared.previousData)?.backupImportReview[0]
  if (!review) return <Loading />
  return (
    <>
      {KINDS.map((kind) => {
        const of = review.kinds.find((k) => k.kind === kind)
        if (!of) return null
        const choice = kinds[kind]
        const held = heldBack(kind, of)
        const set = (change: Partial<KindChoice>) => onKinds({ ...kinds, [kind]: { ...choice, ...change } })
        return (
          <Stack key={kind} sx={{ gap: 0.75, alignItems: 'flex-start' }}>
            <Typography component="h3" variant="subtitle1">
              {t(`import.kinds.${kind}.name`)}
            </Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              {t('import.kinds.counts', { new: of.new, differ: of.differ, same: of.same })}
            </Typography>
            <Segmented
              label={t(`import.kinds.${kind}.modesLabel`, { car: car.name })}
              value={choice.mode}
              onChange={(mode) => set({ mode })}
              options={[
                { value: 'DONT_IMPORT', label: t('import.kinds.modes.DONT_IMPORT') },
                { value: 'ONLY_MISSING', label: t('import.kinds.modes.ONLY_MISSING') },
                { value: 'REPLACE_ALL', label: t('import.kinds.modes.REPLACE_ALL'), disabled: held !== null, reason: held ?? undefined },
                { value: 'CHOOSE', label: t('import.kinds.modes.CHOOSE') },
              ]}
            />
            {held && (
              <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                {held}
              </Typography>
            )}
            <Typography variant="body2" role="status" sx={choice.mode === 'REPLACE_ALL' ? { color: 'error.main' } : undefined}>
              {line(kind, choice, of, target.name)}
            </Typography>
            {choice.mode === 'CHOOSE' && (
              <Button variant="soft" onClick={() => setChoosing(kind)}>
                {t(`import.kinds.${kind}.chooseButton`)}
              </Button>
            )}
            {choosing === kind && (
              <ChooseDialog
                token={token}
                car={car}
                targetId={target.id}
                kind={kind}
                review={of}
                choice={choice}
                tyres={tyres}
                onClose={() => setChoosing(null)}
                onDone={(next) => {
                  set(next)
                  setChoosing(null)
                }}
              />
            )}
          </Stack>
        )
      })}
      <TyreRows car={car} review={review} tyres={tyres} onTyres={onTyres} />
      <MediaRows review={review} media={media} onMedia={onMedia} />
    </>
  )
}

/** A new car's tyres (#339): the same rows, every set new. */
function NewCarTyres({
  token,
  car,
  tyres,
  onTyres,
  media,
  onMedia,
}: {
  token: string
  car: { id: string; name: string }
  tyres: TyreChoice
  onTyres: (tyres: TyreChoice) => void
  media: MediaChoice
  onMedia: (media: MediaChoice) => void
}) {
  const compared = useQuery(BackupImportReviewDocument, { variables: { token, cars: [{ carId: car.id, targetVehicleId: null }] }, fetchPolicy: 'network-only' })
  if (compared.error) return <ErrorMessage error={compared.error} />
  const review = (compared.data ?? compared.previousData)?.backupImportReview[0]
  if (!review) return <Loading />
  return (
    <>
      <TyreRows car={car} review={review} tyres={tyres} onTyres={onTyres} />
      <MediaRows review={review} media={media} onMedia={onMedia} />
    </>
  )
}

/**
 * The media's rows (#339, the owner's choice: boxes): the photos of entries (none when the backup was made without them), the car's
 * picture (unticked when the car has one: ticked, the backup's replaces it for good), the charts, the dashboard (unticked when the car has
 * its own: ticked, it replaces it for everyone who sees the car). A row the backup has nothing for is not shown.
 */
function MediaRows({ review, media, onMedia }: { review: CarReview; media: MediaChoice; onMedia: (media: MediaChoice) => void }) {
  const { t } = useTranslation()
  const ids = useId()
  const m = review.media
  // A picture or dashboard left as the car is: taken unless the car has one of its own, once the comparison says.
  useEffect(() => {
    if (!m || (media.picture !== null && media.dashboard !== null)) return
    onMedia({ ...media, picture: media.picture ?? !m.targetPicture, dashboard: media.dashboard ?? !m.targetDashboard })
  }, [m, media, onMedia])
  if (!m) return null
  const rows: { key: keyof MediaChoice; name: string; hint: string | null; on: boolean; disabled?: boolean; shown: boolean }[] = [
    { key: 'photos', name: t('import.media.photosName'), hint: m.photosLeftOut ? t('import.media.noPhotos') : t('import.media.photosRow'), on: media.photos && !m.photosLeftOut, disabled: m.photosLeftOut, shown: m.photosLeftOut || m.photosNew + m.photosHere > 0 },
    { key: 'picture', name: t('import.media.pictureName'), hint: m.targetPicture ? t('import.media.pictureRow') : null, on: media.picture ?? !m.targetPicture, shown: m.picture },
    { key: 'charts', name: t('import.media.chartsName'), hint: t('import.media.chartsRow'), on: media.charts, shown: m.chartsNew + m.chartsHere > 0 },
    { key: 'dashboard', name: t('import.media.dashboardName'), hint: m.targetDashboard ? t('import.media.dashboardRow') : null, on: media.dashboard ?? !m.targetDashboard, shown: m.dashboard || m.ownDashboard },
  ]
  return (
    <Stack sx={{ gap: 0.5 }}>
      {rows
        .filter((row) => row.shown)
        .map((row) => (
          <Stack key={row.key}>
            <FormControlLabel
              control={
                <Checkbox
                  checked={row.on}
                  disabled={row.disabled}
                  onChange={(e) => onMedia({ ...media, [row.key]: e.target.checked })}
                  slotProps={{ input: { 'aria-describedby': row.hint ? `${ids}-${row.key}` : undefined } }}
                />
              }
              label={row.name}
            />
            {row.hint && (
              <Typography id={`${ids}-${row.key}`} variant="body2" sx={{ color: 'text.secondary', pl: 4 }}>
                {row.hint}
              </Typography>
            )}
          </Stack>
        ))}
    </Stack>
  )
}

/**
 * The tyres' rows (#339, the owner's choice: checkboxes): a box per set of the backup with its season and counts, a set here or one the
 * car's history refuses shown but not choosable with why, and *Lines not tied to a set* last when the backup has any.
 */
function TyreRows({ car, review, tyres, onTyres }: { car: { name: string }; review: CarReview; tyres: TyreChoice; onTyres: (tyres: TyreChoice) => void }) {
  const { t } = useTranslation()
  const keyText = useKeyText()
  const heading = useId()
  const ids = useId()
  const { sets, noSetLines, noSetExpenses } = review.tyres
  // How much of the tyres comes by this choice, for the page's own "nothing chosen" (a car can bring only its tyres).
  const coming = sets.filter((s) => s.state === 'NEW' && !tyres.except.includes(s.id)).length + (tyres.noSetLines && noSetLines > 0 ? 1 : 0)
  useEffect(() => {
    if (tyres.coming !== coming) onTyres({ ...tyres, coming })
  }, [coming, tyres, onTyres])
  if (sets.length === 0 && noSetLines === 0) return null
  const why = (set: (typeof sets)[number]) =>
    set.state === 'HERE'
      ? t('import.tyres.here')
      : set.state === 'REFUSED'
        ? (keyText(set.refusalKey ?? '', (set.refusalArgs ?? {}) as Record<string, unknown>) ?? set.refusalKey ?? '')
        : null
  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 0.5, alignItems: 'flex-start' }}>
      <Typography id={heading} component="h3" variant="subtitle1">
        {t('import.tyres.name')}
      </Typography>
      <Stack role="group" aria-label={t('import.tyres.setsLabel', { car: car.name })}>
        {sets.map((set) => {
          const reason = why(set)
          return (
            <Stack key={set.id}>
              <FormControlLabel
                control={
                  <Checkbox
                    disabled={set.state !== 'NEW'}
                    checked={set.state === 'NEW' && !tyres.except.includes(set.id)}
                    onChange={(e) => onTyres({ ...tyres, except: e.target.checked ? tyres.except.filter((id) => id !== set.id) : [...tyres.except, set.id] })}
                    slotProps={{ input: { 'aria-describedby': `${ids}-${set.id}-counts${reason ? ` ${ids}-${set.id}-why` : ''}` } }}
                  />
                }
                label={set.name}
              />
              <Typography id={`${ids}-${set.id}-counts`} variant="body2" sx={{ color: 'text.secondary', pl: 4 }}>
                {t('import.tyres.setCounts', { season: t(`tyres.seasons.${set.season}`), installations: set.installations, lines: set.lines, tyres: set.tyres })}
              </Typography>
              {reason && (
                <Typography id={`${ids}-${set.id}-why`} variant="body2" sx={{ color: 'text.secondary', pl: 4 }}>
                  {reason}
                </Typography>
              )}
            </Stack>
          )
        })}
        {noSetLines > 0 && (
          <Stack>
            <FormControlLabel
              control={
                <Checkbox
                  checked={tyres.noSetLines}
                  onChange={(e) => onTyres({ ...tyres, noSetLines: e.target.checked })}
                  slotProps={{ input: { 'aria-describedby': `${ids}-no-set` } }}
                />
              }
              label={t('import.tyres.noSet')}
            />
            <Typography id={`${ids}-no-set`} variant="body2" sx={{ color: 'text.secondary', pl: 4 }}>
              {t('import.tyres.noSetCounts', { count: noSetLines, expenses: noSetExpenses })}
            </Typography>
          </Stack>
        )}
      </Stack>
    </Stack>
  )
}

// ---- the Choose… dialog ------------------------------------------------------------------------------------------

const PAGE = 50
type Entry = BackupImportEntriesQuery['backupImportEntries']['items'][number]

const DAYS = new Set(['date', 'lastDoneDate'])
const YES_NO = new Set(['isFullTank', 'missedPreviousFillUp'])
const TEXTS = new Set(['note', 'title', 'category', 'currency'])

/** An entry in a few words (its day, reading and main value), and a differing field's value as the page writes such things. */
function useEntryText() {
  const { t } = useTranslation()
  const { date, number, money } = useFormat()
  const label = (kind: Kind, entry: Entry): string => {
    const parts: string[] = []
    if (kind !== 'refueling' && entry.title) parts.push(entry.title)
    if (entry.date) parts.push(date(entry.date))
    if (entry.odometer != null) parts.push(number(entry.odometer))
    if (entry.volume != null) parts.push(number(entry.volume))
    if (entry.amount != null) parts.push(entry.currency ? money(entry.amount, entry.currency) : number(entry.amount))
    return parts.join(' · ')
  }
  const value = (field: string, text: string | null | undefined): string => {
    if (text == null || text === '') return t('sync.value.empty')
    if (DAYS.has(field)) return date(text)
    if (YES_NO.has(field)) return text === 'true' ? t('sync.value.yes') : t('sync.value.no')
    if (field === 'kind') return t(`recurring.kind.${text as 'TIME'}`, { defaultValue: text })
    if (TEXTS.has(field) || Number.isNaN(Number(text))) return text
    return number(Number(text))
  }
  return { label, value }
}

function ChooseDialog({
  token,
  car,
  targetId,
  kind,
  review,
  choice,
  tyres,
  onClose,
  onDone,
}: {
  token: string
  car: { id: string; name: string }
  targetId: string
  kind: Kind
  review: KindReview
  choice: KindChoice
  tyres: TyreChoice
  onClose: () => void
  onDone: (choice: KindChoice) => void
}) {
  const { t } = useTranslation()
  const phone = useMediaQuery(MEDIA.narrow)
  const [draft, setDraft] = useState<KindChoice>(choice)
  const [shown, setShown] = useState({ fresh: 1, differ: 1 }) // pages of each section
  const [freshHeading, differHeading, sameHeading] = [useId(), useId(), useId()]
  const toggle = (list: string[], id: string) => (list.includes(id) ? list.filter((other) => other !== id) : [...list, id])
  const pages = (count: number) => Array.from({ length: count }, (_, i) => i)
  const left = (total: number, pagesShown: number) => Math.min(PAGE, total - pagesShown * PAGE)
  const query = { token, carId: car.id, targetVehicleId: targetId, kind, tyres: tyresInput(tyres) }
  return (
    <DialogFrame open onClose={onClose} title={t(`import.kinds.${kind}.dialogTitle`, { car: car.name })} maxWidth={720} fullScreen={phone}>
      <Stack sx={{ gap: 2.5 }}>
        <Stack component="section" aria-labelledby={freshHeading} sx={{ gap: 1, alignItems: 'flex-start' }}>
          <Typography id={freshHeading} component="h3" variant="subtitle1">
            {t('import.kinds.dialog.onlyThere', { count: review.new })}
          </Typography>
          {review.new === 0 ? (
            <Typography variant="body2">{t('import.kinds.dialog.none')}</Typography>
          ) : (
            <>
              <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
                <Button variant="soft" color="neutral" onClick={() => setDraft({ ...draft, addNew: true, newExceptions: [] })}>
                  {t('import.kinds.dialog.tickAll')}
                </Button>
                <Button variant="soft" color="neutral" onClick={() => setDraft({ ...draft, addNew: false, newExceptions: [] })}>
                  {t('import.kinds.dialog.untickAll')}
                </Button>
              </Stack>
              <Stack sx={{ alignSelf: 'stretch' }}>
                {pages(shown.fresh).map((page) => (
                  <EntriesPage key={page} variables={{ ...query, differing: false, skip: page * PAGE, take: PAGE }}>
                    {(entry, text) => (
                      <FormControlLabel
                        key={entry.id}
                        control={<Checkbox checked={draft.addNew !== draft.newExceptions.includes(entry.id)} onChange={() => setDraft({ ...draft, newExceptions: toggle(draft.newExceptions, entry.id) })} />}
                        label={text.label(kind, entry)}
                      />
                    )}
                  </EntriesPage>
                ))}
              </Stack>
              {left(review.new, shown.fresh) > 0 && (
                <Button variant="soft" color="neutral" onClick={() => setShown({ ...shown, fresh: shown.fresh + 1 })}>
                  {t('import.kinds.dialog.more', { count: left(review.new, shown.fresh) })}
                </Button>
              )}
            </>
          )}
        </Stack>
        <Stack component="section" aria-labelledby={differHeading} sx={{ gap: 1, alignItems: 'flex-start' }}>
          <Typography id={differHeading} component="h3" variant="subtitle1">
            {t('import.kinds.dialog.differ', { count: review.differ })}
          </Typography>
          {review.differ === 0 ? (
            <Typography variant="body2">{t('import.kinds.dialog.none')}</Typography>
          ) : (
            <>
              <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
                <Button variant="soft" color="neutral" onClick={() => setDraft({ ...draft, takeBackups: false, differExceptions: [] })}>
                  {t('import.kinds.dialog.keepAll')}
                </Button>
                <Button variant="soft" color="neutral" onClick={() => setDraft({ ...draft, takeBackups: true, differExceptions: [] })}>
                  {t('import.kinds.dialog.takeAll')}
                </Button>
              </Stack>
              <Stack sx={{ gap: 2, alignSelf: 'stretch' }}>
                {pages(shown.differ).map((page) => (
                  <EntriesPage key={page} variables={{ ...query, differing: true, skip: page * PAGE, take: PAGE }}>
                    {(entry, text) => (
                      <DifferingEntry
                        key={entry.id}
                        name={text.label(kind, entry)}
                        entry={entry}
                        valueOf={text.value}
                        take={draft.takeBackups !== draft.differExceptions.includes(entry.id)}
                        onTake={(take) => {
                          if (take !== (draft.takeBackups !== draft.differExceptions.includes(entry.id))) setDraft({ ...draft, differExceptions: toggle(draft.differExceptions, entry.id) })
                        }}
                      />
                    )}
                  </EntriesPage>
                ))}
              </Stack>
              {left(review.differ, shown.differ) > 0 && (
                <Button variant="soft" color="neutral" onClick={() => setShown({ ...shown, differ: shown.differ + 1 })}>
                  {t('import.kinds.dialog.more', { count: left(review.differ, shown.differ) })}
                </Button>
              )}
            </>
          )}
        </Stack>
        <Stack component="section" aria-labelledby={sameHeading} sx={{ gap: 1 }}>
          <Typography id={sameHeading} component="h3" variant="subtitle1">
            {t('import.kinds.dialog.same', { count: review.same })}
          </Typography>
          <Typography variant="body2">{t('import.kinds.dialog.nothing')}</Typography>
        </Stack>
        <DialogButtons>
          <DialogCancel />
          <Button onClick={() => onDone(draft)}>{t('import.kinds.dialog.done')}</Button>
        </DialogButtons>
      </Stack>
    </DialogFrame>
  )
}

/** One page of a section, asked for when it is shown. */
function EntriesPage({
  variables,
  children,
}: {
  variables: { token: string; carId: string; targetVehicleId: string; kind: string; differing: boolean; skip: number; take: number; tyres: ReturnType<typeof tyresInput> }
  children: (entry: Entry, text: ReturnType<typeof useEntryText>) => React.ReactNode
}) {
  const text = useEntryText()
  const page = useQuery(BackupImportEntriesDocument, { variables, fetchPolicy: 'network-only' })
  if (page.error) return <ErrorMessage error={page.error} />
  if (!page.data) return <Loading />
  return <>{page.data.backupImportEntries.items.map((entry) => children(entry, text))}</>
}

function DifferingEntry({ name, entry, valueOf, take, onTake }: { name: string; entry: Entry; valueOf: (field: string, text: string | null | undefined) => string; take: boolean; onTake: (take: boolean) => void }) {
  const { t } = useTranslation()
  const label = useId()
  return (
    <Stack sx={{ gap: 0.5 }}>
      <Typography id={label} variant="body1" sx={{ fontWeight: 'fontWeightBold' }}>
        {name}
      </Typography>
      <Box sx={{ overflowX: 'auto' }}>
        <Box component="table" aria-labelledby={label} sx={{ borderCollapse: 'collapse', '& th, & td': { textAlign: 'left', verticalAlign: 'top', pr: 2, py: 0.25 }, typography: 'body2' }}>
          <thead>
            <tr>
              <th scope="col">{t('import.kinds.dialog.field')}</th>
              <th scope="col">{t('import.kinds.dialog.yours')}</th>
              <th scope="col">{t('import.kinds.dialog.backups')}</th>
            </tr>
          </thead>
          <tbody>
            {entry.differences.map((d) => (
              <tr key={d.field}>
                <th scope="row">{t(`sync.fields.${d.field as 'note'}`, { defaultValue: d.field })}</th>
                <td>{valueOf(d.field, d.target)}</td>
                <td>{valueOf(d.field, d.backup)}</td>
              </tr>
            ))}
          </tbody>
        </Box>
      </Box>
      <RadioGroup row aria-labelledby={label} value={take ? 'take' : 'keep'} onChange={(e) => onTake(e.target.value === 'take')}>
        <FormControlLabel value="keep" control={<Radio />} label={t('import.kinds.dialog.keepMine')} />
        <FormControlLabel value="take" control={<Radio />} label={t('import.kinds.dialog.take')} />
      </RadioGroup>
    </Stack>
  )
}

// ---- the typed confirmation of a Replace all ----------------------------------------------------------------------

/** A car with a *Replace all*, as its confirmation says it. */
export interface Replacement {
  carId: string
  /** The own car whose entries go: its name is what is typed. */
  target: string
  /** What goes, per kind with a *Replace all*: live and in the trash together. */
  deleted: { kind: Kind; count: number }[]
  trashed: number
  linksLost: number
  /** The car's photos the deleted entries take along that the backup does not bring back (#339). */
  photosLost: number
}

const sameName = (typed: string, name: string) => typed.trim().toLowerCase() === name.trim().toLowerCase()

/**
 * The typed confirmations before an import that deletes (#338, the owner's decisions 6 and 10): one per car with a *Replace all*, in
 * turn. Cancel on any of them starts nothing; the last one's button starts the import with the names as they were typed.
 */
export function ReplaceConfirmations({ cars, onCancel, onConfirmed }: { cars: Replacement[]; onCancel: () => void; onConfirmed: (names: Record<string, string>) => void }) {
  const { t, i18n } = useTranslation()
  const [index, setIndex] = useState(0)
  const [names, setNames] = useState<Record<string, string>>({})
  const car = cars[index]
  if (!car) return null
  const typed = names[car.carId] ?? ''
  const last = index === cars.length - 1
  const list = new Intl.ListFormat(validLocale(i18n.language, i18n.resolvedLanguage), { type: 'conjunction' }).format(car.deleted.map((d) => t(`import.kinds.${d.kind}.listItem`, { count: d.count })))
  const go = () => {
    if (!sameName(typed, car.target)) return
    if (last) onConfirmed(names)
    else setIndex(index + 1)
  }
  return (
    <DialogFrame
      key={car.carId}
      open
      onClose={onCancel}
      role="alertdialog"
      title={cars.length > 1 ? t('import.kinds.confirm.titleOf', { n: index + 1, count: cars.length }) : t('import.kinds.confirm.title')}
      description={t('import.kinds.confirm.text', { list, car: car.target })}
    >
      <Stack
        component="form"
        noValidate
        sx={{ gap: 1.5 }}
        onSubmit={(e) => {
          e.preventDefault()
          go()
        }}
      >
        {car.trashed > 0 && <Typography variant="body2">{t('import.kinds.confirm.trash', { count: car.trashed })}</Typography>}
        {car.linksLost > 0 && <Typography variant="body2">{t('import.kinds.confirm.links', { count: car.linksLost })}</Typography>}
        {car.photosLost > 0 && <Typography variant="body2">{t('import.media.photosLost', { count: car.photosLost })}</Typography>}
        <Typography variant="body2">{t('import.kinds.confirm.undone')}</Typography>
        <Field name="confirmedName" label={t('import.kinds.confirm.field', { car: car.target })}>
          <FieldInput autoComplete="off" value={typed} onChange={(e) => setNames({ ...names, [car.carId]: e.target.value })} />
        </Field>
        <DialogButtons>
          <DialogCancel autoFocus />
          <Button type="submit" color="error" disabled={!sameName(typed, car.target)}>
            {last ? t('import.kinds.confirm.confirm') : t('import.kinds.confirm.next', { car: cars[index + 1].target })}
          </Button>
        </DialogButtons>
      </Stack>
    </DialogFrame>
  )
}
