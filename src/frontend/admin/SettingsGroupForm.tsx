import { useMutation } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { LabeledSwitch } from '../components/LabeledSwitch.tsx'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import { UpdateSystemSettingsDocument, type SystemSettingChangeInput, type SystemSettingFieldsFragment, type SettingsGroup } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { useToast } from '../toast/toastContext.ts'
import { envName, labelKey, problemsOf } from './settingsText.ts'

type Setting = SystemSettingFieldsFragment

const MODE_KEY = 'Auth:Mode'

/**
 * One group of system settings as a form: the settings the environment holds are shown read-only with their variable, the others are
 * edited and saved together (only what changed is sent; a secret is sent only when typed, or removed). A change of the sign-in mode asks
 * once more and says the way back.
 */
export function SettingsGroupForm({ group }: { group: { name: SettingsGroup; settings: readonly Setting[] } }) {
  const { t, i18n } = useTranslation()
  const { toast } = useToast()
  const heading = useId()
  const [update, { loading: saving }] = useMutation(UpdateSystemSettingsDocument, { refetchQueries: ['SystemSettings'], awaitRefetchQueries: true })
  const [draft, setDraft] = useState<Record<string, string>>(() => Object.fromEntries(group.settings.map((s) => [s.key, s.isSecret ? '' : (s.value ?? '')])))
  const [cleared, setCleared] = useState<ReadonlySet<string>>(new Set())
  const [error, setError] = useState<unknown>()
  const [problems, setProblems] = useState<string[]>([])
  const [confirmMode, setConfirmMode] = useState(false)

  const label = (s: Setting) => (i18n.exists(labelKey(s.key)) ? String(t(labelKey(s.key) as never)) : s.key)
  const editable = group.settings.filter((s) => s.editable)
  const fixed = group.settings.filter((s) => !s.editable)
  const changes: SystemSettingChangeInput[] = editable.flatMap((s) => {
    if (s.isSecret) {
      if (cleared.has(s.key)) return [{ key: s.key, value: null, clear: true }]
      return draft[s.key] ? [{ key: s.key, value: draft[s.key], clear: false }] : []
    }
    const value = draft[s.key] ?? ''
    if (value === (s.value ?? '')) return []
    return [{ key: s.key, value: value || null, clear: value === '' }]
  })
  const modeChange = changes.find((c) => c.key === MODE_KEY)

  async function save() {
    setError(undefined)
    setProblems([])
    try {
      await update({ variables: { changes } })
      toast(t('systemSettings.saved'))
    } catch (e) {
      setError(e)
      setProblems(problemsOf(e))
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (changes.length === 0) return
    if (modeChange) setConfirmMode(true)
    else void save()
  }

  const set = (key: string, value: string) => setDraft((d) => ({ ...d, [key]: value }))

  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 1.5, mt: 3 }}>
      <div>
        <Typography component="h2" variant="h5" id={heading} sx={{ mb: 0.5 }}>
          {t(`systemSettings.groups.${group.name}`)}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t(`systemSettings.groupHelp.${group.name}`)}
        </Typography>
      </div>
      {fixed.length > 0 && (
        <DefinitionList
          items={fixed.map((s) => ({
            label: label(s),
            value: (
              <>
                {shown(s, t)}
                <Typography component="span" variant="caption" sx={{ display: 'block', color: 'text.secondary' }}>
                  {t('systemSettings.source.ENVIRONMENT', { variable: envName(s.key) })}
                </Typography>
              </>
            ),
          }))}
        />
      )}
      {editable.length > 0 && (
        <Form onSubmit={submit} aria-label={t('systemSettings.groupForm', { group: t(`systemSettings.groups.${group.name}`) })}>
          <Stack sx={{ gap: 1.5 }}>
            {editable.map((s) => (
              <SettingControl
                key={s.key}
                setting={s}
                label={label(s)}
                value={draft[s.key] ?? ''}
                cleared={cleared.has(s.key)}
                onChange={(value) => set(s.key, value)}
                onClear={(clear) =>
                  setCleared((c) => {
                    const next = new Set(c)
                    if (clear) next.add(s.key)
                    else next.delete(s.key)
                    return next
                  })
                }
              />
            ))}
            {error !== undefined &&
              (problems.length > 0 ? (
                <ErrorMessage error={error}>
                  {t('systemSettings.problems')}
                  <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
                    {problems.map((p) => (
                      <li key={p}>{p}</li>
                    ))}
                  </Box>
                </ErrorMessage>
              ) : (
                <ErrorMessage error={error} />
              ))}
            <div>
              <Button type="submit" variant="contained" size="large" loading={saving} disabled={changes.length === 0}>
                {t('systemSettings.save')}
              </Button>
            </div>
          </Stack>
        </Form>
      )}
      <DialogFrame open={confirmMode} onClose={() => setConfirmMode(false)} role="alertdialog" title={t('systemSettings.modeChangeTitle')} description={t('systemSettings.modeChangeDescription', { mode: modeChange?.value ?? '' })}>
        <DialogButtons>
          <DialogCancel autoFocus />
          <Button
            color="error"
            onClick={() => {
              setConfirmMode(false)
              void save()
            }}
          >
            {t('systemSettings.confirmModeChange')}
          </Button>
        </DialogButtons>
      </DialogFrame>
    </Stack>
  )
}

/** A read-only value as words: a secret only as set or not, a list on one line, nothing as a dash. */
function shown(s: Setting, t: (key: never, options?: never) => string): string {
  if (s.isSecret) return s.isSet ? t('systemSettings.secretSetShort' as never) : t('systemSettings.secretNotSetShort' as never)
  if (!s.value) return t('common.none' as never)
  return s.kind === 'LIST' ? s.value.split('\n').join(', ') : s.value
}

function SettingControl({
  setting: s,
  label,
  value,
  cleared,
  onChange,
  onClear,
}: {
  setting: Setting
  label: string
  value: string
  cleared: boolean
  onChange: (value: string) => void
  onClear: (clear: boolean) => void
}) {
  const { t } = useTranslation()
  const hint = [envName(s.key), t(`systemSettings.source.${s.source}`, { variable: envName(s.key) }), s.restartPending && t('systemSettings.pendingRestart'), s.kind === 'LIST' && t('systemSettings.listHint')]
    .filter((part): part is string => typeof part === 'string' && part.length > 0)
    .join(' · ')

  switch (s.kind) {
    case 'BOOLEAN':
      return <LabeledSwitch label={label} hint={hint} checked={value === 'true'} onChange={(checked) => onChange(checked ? 'true' : 'false')} />
    case 'CHOICE':
      return (
        <Stack sx={{ gap: 0.5 }}>
          <LabeledSelect
            label={label}
            value={value}
            placeholder={t('systemSettings.useDefault')}
            onChange={onChange}
            options={[{ value: '', label: t('systemSettings.useDefault') }, ...s.choices.map((c) => ({ value: c, label: c }))]}
          />
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {hint}
          </Typography>
        </Stack>
      )
    case 'SECRET':
      return (
        <Stack sx={{ gap: 0.5 }}>
          <Field name={s.key} label={label} hint={`${s.isSet ? t('systemSettings.secretSet') : t('systemSettings.secretNotSet')} · ${hint}`}>
            <FieldInput type="password" autoComplete="new-password" value={value} disabled={cleared} onChange={(e) => onChange(e.target.value)} />
          </Field>
          {s.isSet && <FormControlLabel control={<Checkbox checked={cleared} onChange={(e) => onClear(e.target.checked)} />} label={t('systemSettings.clearSecret')} />}
        </Stack>
      )
    case 'INTEGER':
    case 'DECIMAL': {
      const integer = s.kind === 'INTEGER'
      return (
        <Field
          name={s.key}
          label={label}
          hint={hint}
          invalid={{ message: t(integer ? 'systemSettings.wholeNumber' : 'forms.numberInvalid'), test: (v) => v.trim() !== '' && !(integer ? /^-?\d+$/ : /^-?\d+([.,]\d+)?$/).test(v.trim()) }}
        >
          <FieldInput inputMode={integer ? 'numeric' : 'decimal'} value={value} onChange={(e) => onChange(e.target.value)} />
        </Field>
      )
    }
    case 'MULTILINE':
    case 'LIST':
      return (
        <Field name={s.key} label={label} hint={hint}>
          <FieldInput multiline minRows={3} value={value} onChange={(e) => onChange(e.target.value)} />
        </Field>
      )
    default:
      return (
        <Field name={s.key} label={label} hint={hint}>
          <FieldInput value={value} onChange={(e) => onChange(e.target.value)} />
        </Field>
      )
  }
}
