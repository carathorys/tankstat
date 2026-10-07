import FormHelperText from '@mui/material/FormHelperText'
import FormLabel from '@mui/material/FormLabel'
import OutlinedInput from '@mui/material/OutlinedInput'
import Stack from '@mui/material/Stack'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LabeledSelect } from '../../components/UnitSelect.tsx'
import { Field } from '../../forms/Field.tsx'
import { FieldDate } from '../../forms/FieldDate.tsx'
import { formatRule, parseRule, type OfflineWindow } from '../../offline/offlineWindow.ts'
import { SPAN_PARTS } from './describeWindow.ts'

type Kind = OfflineWindow['kind'] | 'default'
type Parts = Record<(typeof SPAN_PARTS)[number], string>

const MAX_DAYS = 100 * 365.25
const EMPTY: Parts = { years: '', months: '', days: '', hours: '', minutes: '', seconds: '' }

/** The rule the choices make, `null` while they do not make a valid one (and why), or `'default'` for "the same as for every vehicle". */
function ruleOf(kind: Kind, date: string, parts: Parts): { rule: string | null; problem?: 'spanRequired' | 'spanTooLong' } {
  if (kind === 'default') return { rule: 'default' }
  if (kind === 'from') return { rule: date ? formatRule({ kind, date }) : null }
  if (kind !== 'span') return { rule: kind }
  const n = Object.fromEntries(SPAN_PARTS.map((p) => [p, Math.max(0, Math.floor(Number(parts[p]) || 0))])) as Record<keyof Parts, number>
  if (SPAN_PARTS.every((p) => n[p] === 0)) return { rule: null, problem: 'spanRequired' }
  const days = n.years * 365.25 + n.months * (365.25 / 12) + n.days + n.hours / 24 + n.minutes / 1440 + n.seconds / 86400
  if (days > MAX_DAYS) return { rule: null, problem: 'spanTooLong' }
  return { rule: formatRule({ kind, ...n }) }
}

/**
 * Chooses an offline window: a preset, a start date, or a timespan back from now in years down to seconds. Reports the rule it makes,
 * or null while it makes none (an empty timespan); `withDefault` adds "the same as for every vehicle" (reported as `'default'`).
 */
export function WindowPicker({ label, rule, withDefault = false, onChange }: { label: string; rule: string; withDefault?: boolean; onChange: (rule: string | null) => void }) {
  const { t } = useTranslation()
  const spanId = useId()
  const initial = rule === 'default' ? null : parseRule(rule)
  const [kind, setKind] = useState<Kind>(rule === 'default' ? 'default' : (initial?.kind ?? 'span'))
  const [date, setDate] = useState(initial?.kind === 'from' ? initial.date : '')
  const [parts, setParts] = useState<Parts>(initial?.kind === 'span' ? (Object.fromEntries(SPAN_PARTS.map((p) => [p, initial[p] ? String(initial[p]) : ''])) as Parts) : EMPTY)
  const { problem } = ruleOf(kind, date, parts)

  const report = (k: Kind, d: string, p: Parts) => onChange(ruleOf(k, d, p).rule)
  const kinds: Kind[] = [...(withDefault ? (['default'] as const) : []), 'none', 'all', 'thisYear', 'thisAndLastYear', 'from', 'span']

  return (
    <Stack sx={{ gap: 1.5 }}>
      <LabeledSelect<Kind>
        label={label}
        value={kind}
        options={kinds.map((k) => ({ value: k, label: t(`account.offline.kinds.${k}`) }))}
        onChange={(k) => {
          setKind(k)
          report(k, date, parts)
        }}
      />
      {kind === 'from' && (
        <Field name="offline-from" label={t('account.offline.since')} required>
          <FieldDate
            value={date}
            disableFuture
            onChange={(iso) => {
              setDate(iso)
              report(kind, iso, parts)
            }}
          />
        </Field>
      )}
      {kind === 'span' && (
        <Stack component="fieldset" aria-describedby={problem ? `${spanId}-problem` : undefined} sx={{ border: 0, p: 0, m: 0, gap: 1 }}>
          <FormLabel component="legend" sx={{ mb: 1 }}>
            {t('account.offline.spanLegend')}
          </FormLabel>
          <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
            {SPAN_PARTS.map((part) => (
              <Stack key={part} sx={{ gap: 0.5, width: { xs: 'calc(33% - 8px)', sm: 96 } }}>
                <FormLabel htmlFor={`${spanId}-${part}`}>{t(`account.offline.spanParts.${part}`)}</FormLabel>
                <OutlinedInput
                  id={`${spanId}-${part}`}
                  type="number"
                  value={parts[part]}
                  slotProps={{ input: { min: 0, step: 1, inputMode: 'numeric' } }}
                  onChange={(e) => {
                    const next = { ...parts, [part]: e.target.value }
                    setParts(next)
                    report(kind, date, next)
                  }}
                />
              </Stack>
            ))}
          </Stack>
          {problem && (
            <FormHelperText id={`${spanId}-problem`} error>
              {t(`account.offline.${problem}`)}
            </FormHelperText>
          )}
        </Stack>
      )}
    </Stack>
  )
}
