import { useMutation } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import Checkbox from '@mui/material/Checkbox'
import Collapse from '@mui/material/Collapse'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { ChevronDown } from 'lucide-react'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { DialogButtons, DialogCancel } from '../../dialogs/DialogFrame.tsx'
import { Form } from '../../forms/Form.tsx'
import { NewPasswordFields } from '../../forms/NewPasswordFields.tsx'
import { StartExportDocument, type ExportFileFieldsFragment } from '../../gql/generated.ts'
import { useFormat } from '../../i18n/format.ts'
import { ErrorMessage, WarningMessage } from '../../messages.tsx'
import { ALL_KINDS, estimate, inputOf, kindsOf, takesAnything, type ExportKind, type ExportKinds, type ExportOffer } from './exportChoices.ts'

/**
 * What to export (#341), on the Export tab (every own car, each ticked by its box) and in a car's Export this car dialog (`single`: that
 * car, always taken): the kinds per car behind *Choose what to export*, the optional passphrase typed twice, the notes, the estimated size,
 * and *Export*. A file of the person's kept or being built is said to be replaced (`current`).
 */
export function ExportForm({
  cars,
  single = false,
  minLength,
  current,
  onStarted,
}: {
  cars: readonly ExportOffer[]
  single?: boolean
  minLength: number
  current?: ExportFileFieldsFragment | null
  onStarted: () => void
}) {
  const { t } = useTranslation()
  const { bytes, dateTime } = useFormat()
  const [ticked, setTicked] = useState<ReadonlySet<string>>(() => new Set(single ? cars.map((c) => c.id) : []))
  const [kinds, setKinds] = useState<Readonly<Record<string, ExportKinds>>>({})
  const [start, started] = useMutation(StartExportDocument, { refetchQueries: ['MyExport'], awaitRefetchQueries: true })
  const [error, setError] = useState<unknown>()

  const chosen = cars.filter((c) => ticked.has(c.id))
  const kindsOfCar = (car: ExportOffer) => kinds[car.id] ?? ALL_KINDS
  const empty = chosen.find((c) => !takesAnything(c, kindsOfCar(c)))
  const reason = chosen.length === 0 ? t('export.noCar') : empty ? t('export.noKind', { car: empty.name }) : undefined
  const size = chosen.reduce((sum, c) => sum + estimate(c, kindsOfCar(c)), 0)
  const replaces =
    current?.state === 'BUILDING' ? t('export.replacesBuilding') : current?.state === 'READY' && current.keptUntil ? t('export.replacesKept', { time: dateTime(current.keptUntil) }) : undefined

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    if (reason) return
    const form = new FormData(e.currentTarget)
    // Only spaces is no passphrase, as the fields take it: the file is then plain.
    const typedOrNone = (value: FormDataEntryValue | null) => (String(value ?? '').trim() === '' ? null : String(value))
    setError(undefined)
    try {
      await start({
        variables: {
          input: {
            cars: chosen.map((c) => inputOf(c, kindsOfCar(c))),
            passphrase: typedOrNone(form.get('passphrase')),
            passphraseConfirmation: typedOrNone(form.get('passphraseRepeat')),
          },
        },
      })
      onStarted()
    } catch (err) {
      setError(err)
    }
  }

  const exportButton = (
    <Button size="large" type="submit" disabled={reason !== undefined || started.loading} loading={started.loading}>
      {t('export.export')}
    </Button>
  )
  return (
    <Form onSubmit={(e) => void submit(e)} refusal={error}>
      <Stack sx={{ gap: 2 }}>
        {cars.map((car) => (
          <CarChoice
            key={car.id}
            car={car}
            single={single}
            ticked={ticked.has(car.id)}
            onTicked={(on) => setTicked((was) => (on ? new Set([...was, car.id]) : new Set([...was].filter((id) => id !== car.id))))}
            kinds={kindsOfCar(car)}
            onKinds={(next) => setKinds((was) => ({ ...was, [car.id]: next }))}
          />
        ))}
        <Card component="fieldset" sx={{ p: 1.5, m: 0, border: 0, minWidth: 0 }}>
          <Typography component="legend" variant="body2" sx={{ fontWeight: 700, p: 0, mb: 0.5, float: 'left', width: '100%' }}>
            {t('export.passphrase')}
          </Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1.5, clear: 'both' }}>
            {t('export.passphraseHint')}
          </Typography>
          <Stack sx={{ gap: 1.5, maxWidth: '28rem' }}>
            <NewPasswordFields
              name="passphrase"
              label={t('export.passphrase')}
              repeatLabel={t('export.passphraseRepeat')}
              showLabel={t('forms.password.showPassphrase')}
              required={false}
              minLength={minLength}
              hint={t('export.passphraseMin', { min: minLength })}
              tooShort={t('errors.backup.passphraseTooShort', { min: minLength })}
            />
          </Stack>
        </Card>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t('export.notes')}
        </Typography>
        {replaces && <WarningMessage>{replaces}</WarningMessage>}
        {error !== undefined && <ErrorMessage error={error} />}
        {reason && (
          <Typography variant="body2" role="status">
            {reason}
          </Typography>
        )}
        {single ? (
          <DialogButtons>
            <Typography variant="body2" sx={{ mr: 'auto' }}>
              {t('export.total', { count: chosen.length, size: bytes(size) })}
            </Typography>
            <DialogCancel disabled={started.loading} />
            {exportButton}
          </DialogButtons>
        ) : (
          <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography variant="body2">{t('export.total', { count: chosen.length, size: bytes(size) })}</Typography>
            {exportButton}
          </Stack>
        )}
      </Stack>
    </Form>
  )
}

/** A car of the choice: its box (none in a car's own dialog) and counts, and once ticked its kinds behind a disclosure. */
function CarChoice({
  car,
  single,
  ticked,
  onTicked,
  kinds,
  onKinds,
}: {
  car: ExportOffer
  single: boolean
  ticked: boolean
  onTicked: (on: boolean) => void
  kinds: ExportKinds
  onKinds: (kinds: ExportKinds) => void
}) {
  const { t } = useTranslation()
  const { bytes } = useFormat()
  const [open, setOpen] = useState(false)
  const kindsId = useId()
  const name = car.licensePlate ? `${car.name} (${car.licensePlate})` : car.name
  const counts = [
    car.refuelings > 0 && t('export.counts.refuelings', { count: car.refuelings }),
    car.expenses > 0 && t('export.counts.expenses', { count: car.expenses }),
    car.schedules > 0 && t('export.counts.schedules', { count: car.schedules }),
    car.tyreSets > 0 && t('export.counts.tyreSets', { count: car.tyreSets }),
    car.photos > 0 && t('export.counts.photos', { count: car.photos }),
  ].filter((c): c is string => typeof c === 'string')
  const detail: Record<ExportKind, string> = {
    refuelings: t('export.counts.refuelings', { count: car.refuelings }),
    expenses: t('export.counts.expenses', { count: car.expenses }),
    schedules: t('export.counts.schedules', { count: car.schedules }),
    tyres: t('export.counts.sets', { count: car.tyreSets }),
    charts: t('export.counts.charts', { count: car.charts }),
    photos: [t('export.counts.photos', { count: car.photos }), car.photoBytes > 0 ? t('export.adds', { size: bytes(car.photoBytes) }) : null].filter(Boolean).join(' · '),
    picture: t('export.adds', { size: bytes(car.pictureBytes) }),
  }

  return (
    <Card sx={{ p: 1.5 }}>
      {single ? (
        <Typography sx={{ fontWeight: 700 }}>{name}</Typography>
      ) : (
        <FormControlLabel control={<Checkbox checked={ticked} onChange={(e) => onTicked(e.target.checked)} />} label={name} sx={{ minHeight: 44, ml: -1 }} />
      )}
      {counts.length > 0 && (
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {counts.join(' · ')}
        </Typography>
      )}
      {ticked && (
        <Box sx={{ mt: 1 }}>
          <Button type="button" variant="text" size="small" aria-expanded={open} aria-controls={kindsId} onClick={() => setOpen((was) => !was)} sx={{ minHeight: 44 }}>
            <Box component="span" sx={{ display: 'inline-flex', transition: 'transform 0.2s', transform: open ? 'rotate(180deg)' : 'none' }}>
              <ChevronDown size={16} aria-hidden />
            </Box>
            {t('export.choose')}
          </Button>
          {!open && (
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              {t('export.everything')}
            </Typography>
          )}
          <Collapse in={open}>
            <Stack id={kindsId} role="group" aria-label={t('export.choose')} sx={{ pl: 1 }}>
              {kindsOf(car).map((kind) => (
                <FormControlLabel
                  key={kind}
                  control={<Checkbox checked={kinds[kind]} onChange={(e) => onKinds({ ...kinds, [kind]: e.target.checked })} />}
                  label={`${t(`export.kinds.${kind}`)} · ${detail[kind]}`}
                  sx={{ minHeight: 44, ml: -1 }}
                />
              ))}
            </Stack>
          </Collapse>
        </Box>
      )}
    </Card>
  )
}
