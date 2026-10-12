import { useMutation } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { TFunction } from 'i18next'
import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { LabeledSwitch } from '../components/LabeledSwitch.tsx'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import { SystemSettingsDocument, UpdateSystemSettingsDocument, type SystemSettingChangeInput, type SystemSettingFieldsFragment, type SettingsGroup } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { useToast } from '../toast/toastContext.ts'
import { envName, labelKey, problemsOf } from './settingsText.ts'

type Setting = SystemSettingFieldsFragment

const MODE_KEY = 'Auth:Mode'

/** What a group's form starts with: every value as the server has it, a secret blank (it is never shown back). */
const draftOf = (settings: readonly Setting[]) => Object.fromEntries(settings.map((s) => [s.key, s.isSecret ? '' : (s.value ?? '')]))

/** A switch's value as the server reads it: true in any case is on (.NET reads a JSON `true` written in the file as "True"). */
const isOn = (value: string) => value.trim().toLowerCase() === 'true'

/** A value as it is sent: a decimal with a dot, which is what the server parses (invariantly), however it was typed ("0,5"). */
const sent = (s: Setting, value: string) => (s.kind === 'DECIMAL' ? value.trim().replace(',', '.') : value)

/** Whether a value says what the server has: a switch by on or off (not set is off), a choice in any case (as the server takes it), a decimal as sent. */
function unchanged(s: Setting, value: string): boolean {
  const server = s.value ?? ''
  if (s.kind === 'BOOLEAN') return isOn(value) === isOn(server)
  if (s.kind === 'CHOICE') return value.toLowerCase() === server.toLowerCase()
  return sent(s, value) === sent(s, server)
}

/**
 * One group of system settings as a form: the settings the environment holds are shown read-only with their variable, the others are
 * edited and saved together (only what changed is sent; a secret is sent only when typed, or removed). A change of the sign-in mode asks
 * once more and says the way back.
 */
export function SettingsGroupForm({ group }: { group: { name: SettingsGroup; settings: readonly Setting[] } }) {
  const { t, i18n } = useTranslation()
  const { toast } = useToast()
  const heading = useId()
  const saveButton = useRef<HTMLButtonElement>(null)
  const [update, { loading: saving }] = useMutation(UpdateSystemSettingsDocument, {
    // The answer is the whole page as the server now has it, so it takes the query's place: no second request, whose failure would show a
    // save that was written as refused (and invite another, which writes the file and its .bak once more).
    update: (cache, { data }) => {
      if (data) cache.writeQuery({ query: SystemSettingsDocument, data: { systemSettings: data.updateSystemSettings } })
    },
  })
  const [draft, setDraft] = useState<Record<string, string>>(() => draftOf(group.settings))
  const [cleared, setCleared] = useState<ReadonlySet<string>>(new Set())
  const [error, setError] = useState<unknown>()
  const [confirmMode, setConfirmMode] = useState(false)
  const problems = problemsOf(error)

  // Saving turns the button off, so the focus it had (or that the mode's confirmation gives back to it as it closes) is lost. After a
  // refusal, once the confirmation is gone, it returns there, unless it went somewhere else meanwhile.
  const [confirmGone, setConfirmGone] = useState(true)
  useEffect(() => {
    if (confirmGone && !saving && error !== undefined && (!document.activeElement || document.activeElement === document.body)) saveButton.current?.focus()
  }, [confirmGone, saving, error])

  const label = (s: Setting) => (i18n.exists(labelKey(s.key)) ? String(t(labelKey(s.key) as never)) : s.key)
  const editable = group.settings.filter((s) => s.editable)
  const fixed = group.settings.filter((s) => !s.editable)
  const changes: SystemSettingChangeInput[] = editable.flatMap((s) => {
    if (s.isSecret) {
      if (cleared.has(s.key)) return [{ key: s.key, value: null, clear: true }]
      return draft[s.key] ? [{ key: s.key, value: draft[s.key], clear: false }] : []
    }
    const value = sent(s, draft[s.key] ?? '')
    if (unchanged(s, value)) return []
    return [{ key: s.key, value: value || null, clear: value === '' }]
  })
  const modeChange = changes.find((c) => c.key === MODE_KEY)
  // What the confirmation calls the next mode. Cleared, it is the one below the file (the shipped appsettings.json's None, unless the
  // operator's says otherwise); None, chosen or not, is said for what it is: no sign-in, and so no administrator to change it back here.
  const nextMode = modeChange?.value == null ? t('systemSettings.modeDefault') : modeChange.value.toLowerCase() === 'none' ? t('systemSettings.modeNone') : modeChange.value

  async function save() {
    setError(undefined)
    try {
      const { data } = await update({ variables: { changes } })
      // The form starts afresh from the answer. One that looks as before (a secret typed again, a value the server trimmed, a default
      // cleared that the file never held) keeps this form mounted, and what was typed must not stay as if it were still to be saved.
      setDraft(draftOf(data?.updateSystemSettings.groups.find((g) => g.name === group.name)?.settings ?? group.settings))
      setCleared(new Set())
      toast(t('systemSettings.saved'))
    } catch (e) {
      setError(e)
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
                  <ProblemList problems={problems} />
                </ErrorMessage>
              ) : (
                <ErrorMessage error={error} />
              ))}
            <div>
              <Button ref={saveButton} type="submit" variant="contained" size="large" loading={saving} disabled={changes.length === 0}>
                {t('systemSettings.save')}
              </Button>
            </div>
          </Stack>
        </Form>
      )}
      <ConfirmDialog
        open={confirmMode}
        onOpenChange={setConfirmMode}
        title={t('systemSettings.modeChangeTitle')}
        description={t('systemSettings.modeChangeDescription', { mode: nextMode })}
        confirmLabel={t('systemSettings.confirmModeChange')}
        onConfirm={() => {
          setConfirmGone(false)
          void save()
        }}
        onClosed={() => setConfirmGone(true)}
      />
    </Stack>
  )
}

/** The settings at fault, as the server named them (a refused save or restart, the file left out at start); nothing when there are none. */
export function ProblemList({ problems }: { problems: readonly string[] }) {
  if (problems.length === 0) return null
  return (
    <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
      {problems.map((p) => (
        <li key={p}>{p}</li>
      ))}
    </Box>
  )
}

/** A read-only value as words: a secret only as set or not, a list on one line, nothing as a dash. */
function shown(s: Setting, t: TFunction): string {
  if (s.isSecret) return s.isSet ? t('systemSettings.secretSetShort') : t('systemSettings.secretNotSetShort')
  if (!s.value) return t('common.none')
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
      return <LabeledSwitch label={label} hint={hint} checked={isOn(value)} onChange={(checked) => onChange(checked ? 'true' : 'false')} />
    case 'CHOICE': {
      // The server takes a choice in any case, and spellings it no longer offers ("Ssl"): such a value shows as the choice it means, or as
      // it is written, never as an empty field.
      const choice = s.choices.find((c) => c.toLowerCase() === value.toLowerCase()) ?? value
      const options = [{ value: '', label: t('systemSettings.useDefault') }, ...s.choices.map((c) => ({ value: c, label: c }))]
      if (choice !== '' && !s.choices.includes(choice)) options.push({ value: choice, label: choice })
      return <LabeledSelect label={label} hint={hint} value={choice} placeholder={t('systemSettings.useDefault')} onChange={onChange} options={options} />
    }
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
