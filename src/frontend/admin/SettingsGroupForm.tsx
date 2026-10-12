import { useMutation } from '@apollo/client/react'
import Accordion from '@mui/material/Accordion'
import AccordionDetails from '@mui/material/AccordionDetails'
import AccordionSummary from '@mui/material/AccordionSummary'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { TFunction } from 'i18next'
import { ChevronDown, Info } from 'lucide-react'
import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { DefinitionList } from '../components/DefinitionList.tsx'
import type { LabelBadge } from '../components/LabelBadges.tsx'
import { LabeledSwitch } from '../components/LabeledSwitch.tsx'
import { LabeledSelect } from '../components/UnitSelect.tsx'
import { Field } from '../forms/Field.tsx'
import { FieldInput } from '../forms/FieldInput.tsx'
import { Form } from '../forms/Form.tsx'
import { SystemSettingsDocument, UpdateSystemSettingsDocument, type SystemSettingFieldsFragment, type SettingsGroup } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { useToast } from '../toast/toastContext.ts'
import { changesOf, isOn, type GroupDraft } from './settingsDraft.ts'
import { SettingHelp } from './SettingHelp.tsx'
import { labelKey, problemsOf } from './settingsText.ts'
import { sharedReason, unusedReason, type Unused, type ValueOf } from './settingsRelevance.ts'
import { layoutOf, type SectionId } from './settingsSections.ts'
import { useUnusedText } from './useUnusedText.ts'

type Setting = SystemSettingFieldsFragment

const MODE_KEY = 'Auth:Mode'

/**
 * One group of system settings as a form: the settings the environment holds are shown read-only with their variable, the others are
 * edited and saved together. The values live with the page (`draft`, `onDraft`), so they outlast a switch of tabs, and a setting another
 * one leaves unused (`valueOf`, the values of every group as the page holds them) is shown but cannot be changed, with the reason. A change
 * of the sign-in mode asks once more and says the way back.
 */
export function SettingsGroupForm({
  group,
  draft,
  valueOf,
  onDraft,
  onSaved,
}: {
  group: { name: SettingsGroup; appliesAtOnce: boolean; settings: readonly Setting[] }
  draft: GroupDraft
  valueOf: ValueOf
  onDraft: (draft: GroupDraft) => void
  /** After a save: the form starts afresh from the server's answer, even one that looks as before. */
  onSaved: () => void
}) {
  const { t, i18n } = useTranslation()
  const { toast } = useToast()
  const unusedText = useUnusedText()
  const heading = useId()
  const saveButton = useRef<HTMLButtonElement>(null)
  const [update, { loading: saving }] = useMutation(UpdateSystemSettingsDocument, {
    // The answer is the whole page as the server now has it, so it takes the query's place: no second request, whose failure would show a
    // save that was written as refused (and invite another, which writes the file and its .bak once more).
    update: (cache, { data }) => {
      if (data) cache.writeQuery({ query: SystemSettingsDocument, data: { systemSettings: data.updateSystemSettings } })
    },
  })
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
  const changes = changesOf(group.settings, draft)
  const layout = layoutOf(group.name, group.settings)
  // Which sections are open: as the person left them, else open while used and closed while not (so a choice that leaves one unused folds it).
  const [opened, setOpened] = useState<Partial<Record<SectionId, boolean>>>({})
  const modeChange = changes.find((c) => c.key === MODE_KEY)
  // What the confirmation calls the next mode. Cleared, it is the one below the file (the shipped appsettings.json's None, unless the
  // operator's says otherwise); None, chosen or not, is said for what it is: no sign-in, and so no administrator to change it back here.
  const nextMode = modeChange?.value == null ? t('systemSettings.modeDefault') : modeChange.value.toLowerCase() === 'none' ? t('systemSettings.modeNone') : modeChange.value

  // Why each setting is not used now, said once at the widest place it holds for: above the group, in a section's header, or at the setting.
  const unused = new Map<string, Unused | null>(group.settings.map((s) => [s.key, unusedReason(s.key, valueOf)]))
  const wholeGroup = sharedReason([...unused.values()])
  const sectionReason = new Map(layout.sections.map((s) => [s.id, sharedReason(s.settings.map((setting) => unused.get(setting.key) ?? null))]))
  const reasonOf = (s: Setting, section?: SectionId) => {
    const reason = unused.get(s.key)
    if (!reason || wholeGroup || (section && sectionReason.get(section))) return null
    return unusedText(reason)
  }

  /** A run of settings: the ones the environment holds as values with their variable, the others as controls. */
  const block = (settings: readonly Setting[], section?: SectionId) => {
    const fixed = settings.filter((s) => !s.editable)
    return (
      <Stack sx={{ gap: 1.5 }}>
        {fixed.length > 0 && (
          <DefinitionList
            items={fixed.map((s) => ({
              label: label(s),
              value: (
                <Stack direction="row" sx={{ gap: 0.5, alignItems: 'flex-start' }}>
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    {shown(s, t)}
                    <Typography component="span" variant="caption" sx={{ display: 'block', color: 'text.secondary' }}>
                      {[t('systemSettings.source.ENVIRONMENT'), reasonOf(s, section)].filter(Boolean).join(' ')}
                    </Typography>
                  </Box>
                  <SettingHelp settingKey={s.key} label={label(s)} />
                </Stack>
              ),
            }))}
          />
        )}
        {settings
          .filter((s) => s.editable)
          .map((s) => (
            <SettingControl
              key={s.key}
              setting={s}
              label={label(s)}
              value={draft.values[s.key] ?? ''}
              cleared={draft.cleared.has(s.key)}
              unused={unused.get(s.key) != null}
              reason={reasonOf(s, section)}
              onChange={(value) => set(s.key, value)}
              onClear={(removed) => clear(s.key, removed)}
            />
          ))}
      </Stack>
    )
  }

  const body = (
    <Stack sx={{ gap: 1.5 }}>
      {layout.lead.length > 0 && block(layout.lead)}
      {layout.sections.length > 0 && (
        <div>
          {layout.sections.map((section) => {
            const reason = sectionReason.get(section.id) ?? null
            const pending = changesOf(section.settings, draft).length > 0
            const status = [reason && (wholeGroup ? t('systemSettings.unused.short') : unusedText(reason)), pending && t('systemSettings.sectionUnsaved')].filter(
              (part): part is string => typeof part === 'string',
            )
            return (
              <Accordion
                key={section.id}
                expanded={opened[section.id] ?? !reason}
                onChange={(_, open) => setOpened((o) => ({ ...o, [section.id]: open }))}
                disableGutters
                variant="outlined"
                slotProps={{ heading: { component: 'h3' } }}
              >
                <AccordionSummary expandIcon={<ChevronDown size={18} aria-hidden />} id={`${heading}-${section.id}-header`} aria-controls={`${heading}-${section.id}-panel`}>
                  <Stack sx={{ minWidth: 0 }}>
                    <Typography component="span" variant="body1" sx={{ fontWeight: 700, color: reason ? 'text.secondary' : undefined }}>
                      {t(`systemSettings.sections.${section.id}`)}
                    </Typography>
                    {/* A space between the title and the status, so the header reads as two phrases, not one word. */}{' '}
                    {status.length > 0 && (
                      <Typography component="span" variant="caption" sx={{ color: 'text.secondary' }}>
                        {status.join(' · ')}
                      </Typography>
                    )}
                  </Stack>
                </AccordionSummary>
                <AccordionDetails id={`${heading}-${section.id}-panel`}>{block(section.settings, section.id)}</AccordionDetails>
              </Accordion>
            )
          })}
        </div>
      )}
    </Stack>
  )

  async function save() {
    setError(undefined)
    try {
      const answer = await update({ variables: { changes } })
      onSaved()
      // The server says what waits: a group that applies at once runs with what was saved, and a value saved back to what runs waits for nothing.
      const saved = answer.data?.updateSystemSettings.groups.find((g) => g.name === group.name)
      toast(t(saved?.settings.some((s) => s.restartPending) ? 'systemSettings.saved' : 'systemSettings.savedInEffect'))
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

  const set = (key: string, value: string) => onDraft({ ...draft, values: { ...draft.values, [key]: value } })
  const clear = (key: string, removed: boolean) => {
    const next = new Set(draft.cleared)
    if (removed) next.add(key)
    else next.delete(key)
    onDraft({ ...draft, cleared: next })
  }

  return (
    <Stack component="section" aria-labelledby={heading} sx={{ gap: 1.5 }}>
      <div>
        <Typography component="h2" variant="h5" id={heading} sx={{ mb: 0.5 }}>
          {t(`systemSettings.groups.${group.name}`)}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {t(`systemSettings.groupHelp.${group.name}`)}
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
          {t(group.appliesAtOnce ? 'systemSettings.appliesAtOnce' : 'systemSettings.appliesAfterRestart')}
        </Typography>
      </div>
      {wholeGroup && (
        <Alert severity="info" role="note" icon={<Info size={16} aria-hidden />}>
          {t('systemSettings.unused.group', { reason: unusedText(wholeGroup) })}
        </Alert>
      )}
      {editable.length === 0 && body}
      {editable.length > 0 && (
        <Form onSubmit={submit} aria-label={t('systemSettings.groupForm', { group: t(`systemSettings.groups.${group.name}`) })}>
          <Stack sx={{ gap: 1.5 }}>
            {body}
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

/**
 * One setting's control. `unused`: another setting leaves it unused now, so it is shown but cannot be changed; `reason` says why in its
 * hint (a disabled control takes no focus, so the reason is written out next to it, not left to a tooltip). Null when the group says it once.
 */
function SettingControl({
  setting: s,
  label,
  value,
  cleared,
  unused,
  reason,
  onChange,
  onClear,
}: {
  setting: Setting
  label: string
  value: string
  cleared: boolean
  unused: boolean
  reason: string | null
  onChange: (value: string) => void
  onClear: (clear: boolean) => void
}) {
  return (
    // The question mark beside the control, level with its label: what the setting does and its variable, kept out of the way.
    <Stack direction="row" sx={{ gap: 0.5, alignItems: 'flex-start' }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Control setting={s} label={label} value={value} cleared={cleared} unused={unused} reason={reason} onChange={onChange} onClear={onClear} />
      </Box>
      <SettingHelp settingKey={s.key} label={label} />
    </Stack>
  )
}

function Control({
  setting: s,
  label,
  value,
  cleared,
  unused,
  reason,
  onChange,
  onClear,
}: {
  setting: Setting
  label: string
  value: string
  cleared: boolean
  unused: boolean
  reason: string | null
  onChange: (value: string) => void
  onClear: (clear: boolean) => void
}) {
  const { t } = useTranslation()
  // Where the value comes from, and a restart it waits for, beside the label; the hint keeps only what needs a sentence.
  const badges: LabelBadge[] = [
    { text: t(`systemSettings.sourceShort.${s.source}`), spoken: t(`systemSettings.source.${s.source}`) },
    ...(s.restartPending ? [{ text: t('systemSettings.restartShort'), spoken: t('systemSettings.pendingRestart'), tone: 'warning' as const }] : []),
  ]
  const hint =
    [reason, s.kind === 'LIST' && t('systemSettings.listHint')].filter((part): part is string => typeof part === 'string' && part.length > 0).join(' · ') ||
    undefined

  switch (s.kind) {
    case 'BOOLEAN':
      return <LabeledSwitch label={label} hint={hint} badges={badges} checked={isOn(value)} disabled={unused} onChange={(checked) => onChange(checked ? 'true' : 'false')} />
    case 'CHOICE': {
      // The server takes a choice in any case, and spellings it no longer offers ("Ssl"): such a value shows as the choice it means, or as
      // it is written, never as an empty field.
      const choice = s.choices.find((c) => c.toLowerCase() === value.toLowerCase()) ?? value
      const options = [{ value: '', label: t('systemSettings.useDefault') }, ...s.choices.map((c) => ({ value: c, label: c }))]
      if (choice !== '' && !s.choices.includes(choice)) options.push({ value: choice, label: choice })
      return <LabeledSelect label={label} hint={hint} badges={badges} value={choice} placeholder={t('systemSettings.useDefault')} disabled={unused} onChange={onChange} options={options} />
    }
    case 'SECRET':
      return (
        <Stack sx={{ gap: 0.5 }}>
          <Field name={s.key} label={label} badges={badges} hint={[s.isSet ? t('systemSettings.secretSet') : t('systemSettings.secretNotSet'), hint].filter(Boolean).join(' · ')}>
            <FieldInput type="password" autoComplete="new-password" value={value} disabled={cleared || unused} onChange={(e) => onChange(e.target.value)} />
          </Field>
          {s.isSet && (
            <FormControlLabel control={<Checkbox checked={cleared} disabled={unused} onChange={(e) => onClear(e.target.checked)} />} label={t('systemSettings.clearSecret')} />
          )}
        </Stack>
      )
    case 'INTEGER':
    case 'DECIMAL': {
      const integer = s.kind === 'INTEGER'
      return (
        <Field
          name={s.key}
          label={label}
          badges={badges}
          hint={hint}
          invalid={{ message: t(integer ? 'systemSettings.wholeNumber' : 'forms.numberInvalid'), test: (v) => v.trim() !== '' && !(integer ? /^-?\d+$/ : /^-?\d+([.,]\d+)?$/).test(v.trim()) }}
        >
          <FieldInput inputMode={integer ? 'numeric' : 'decimal'} value={value} disabled={unused} onChange={(e) => onChange(e.target.value)} />
        </Field>
      )
    }
    case 'MULTILINE':
    case 'LIST':
      return (
        <Field name={s.key} label={label} badges={badges} hint={hint}>
          <FieldInput multiline minRows={3} value={value} disabled={unused} onChange={(e) => onChange(e.target.value)} />
        </Field>
      )
    default:
      return (
        <Field name={s.key} label={label} badges={badges} hint={hint}>
          <FieldInput value={value} disabled={unused} onChange={(e) => onChange(e.target.value)} />
        </Field>
      )
  }
}
