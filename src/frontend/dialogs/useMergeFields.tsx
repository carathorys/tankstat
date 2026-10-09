import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { FieldMessage } from '../forms/Field.tsx'
import type { MergeInfo } from './changeEdit.ts'

/**
 * The fields of a dialog that merges a parked edit with what is on the server now (`ChangeEdit.merge`): under a field changed on both
 * sides, the value it does not hold ("On the server: 41 L · Use it", then "Yours: 45 L · Use it"); under one changed on one side only,
 * which side it came from. "Use it" goes through the field's own setter (`set`) for a controlled field; an uncontrolled one (read when the
 * form is sent) starts again from the chosen value (`value`, `key`), so the fields around it keep what was typed. Without `merge`, nothing.
 */
export function useMergeFields(merge: MergeInfo | undefined) {
  const { t } = useTranslation()
  const [taken, setTaken] = useState<Record<string, 'mine' | 'theirs'>>({})
  const [chosen, setChosen] = useState<Record<string, unknown>>({})
  const [round, setRound] = useState<Record<string, number>>({})

  /** What an uncontrolled field starts from: the value taken last for it, else its own. */
  const value = <T,>(field: string, initial: T): T => (field in chosen ? (chosen[field] as T) : initial)
  /** The key that makes an uncontrolled field start again from `value` when a value is taken for it. */
  const key = (field: string) => `${field}-${round[field] ?? 0}`

  /** The note under `field`; `set` puts a taken value into a controlled field (none: it is uncontrolled). */
  const note = (field: string, label: string, set?: (value: unknown) => void): ReactNode => {
    if (!merge) return null
    const conflict = merge.conflicts[field]
    if (conflict) {
      const holding = taken[field] ?? 'mine'
      const other = holding === 'mine' ? 'theirs' : 'mine'
      const side = conflict[other]
      const use = () => {
        if (set) set(side.value)
        else {
          setChosen((c) => ({ ...c, [field]: side.value }))
          setRound((r) => ({ ...r, [field]: (r[field] ?? 0) + 1 }))
        }
        setTaken((v) => ({ ...v, [field]: other }))
      }
      const text = t(other === 'theirs' ? 'sync.merge.onServer' : 'sync.merge.yours', { value: side.text })
      return (
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <FieldMessage sx={{ color: 'text.primary' }}>
            {t('sync.merge.bothChanged')} {text}
          </FieldMessage>
          <Button type="button" variant="soft" sx={{ minHeight: 44 }} aria-label={t('sync.merge.useAria', { field: label, value: side.text })} onClick={use}>
            {t('sync.merge.use')}
          </Button>
        </Stack>
      )
    }
    const from = merge.merged[field]
    return from ? <FieldMessage>{t(from === 'theirs' ? 'sync.merge.fromServer' : 'sync.merge.fromMine')}</FieldMessage> : null
  }

  return { value, key, note }
}
