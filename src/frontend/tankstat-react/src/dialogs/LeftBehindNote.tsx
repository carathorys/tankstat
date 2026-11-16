import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useFormat } from '../i18n/format.ts'
import { usePendingCount } from '../offline/outbox.ts'
import type { ChangeEdit } from './changeEdit.ts'
import { useLeftBehindLabels } from './leftBehindLabels.ts'

/** The changes an edit leaves behind, said as the approved sentence: one or two inline, a list from three on (#330). */
export function LeftBehindText({ labels }: { labels: readonly string[] }) {
  const { t } = useTranslation()
  const format = useFormat()
  if (labels.length < 3) return <>{t('sync.leftBehind.note', { count: labels.length, list: format.list([...labels]) })}</>
  return (
    <>
      {t('sync.leftBehind.noteList', { count: labels.length })}
      <LeftBehindList labels={labels} />
    </>
  )
}

/** The changes as a list (three or more). */
export function LeftBehindList({ labels }: { labels: readonly string[] }) {
  return (
    <Box component="ul" sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
      {labels.map((label, n) => (
        <li key={`${n}:${label}`}>{label}</li>
      ))}
    </Box>
  )
}

/**
 * Above an edit's buttons on Waiting to sync (#330, the owner's decisions 1, 4 and 5): the changes waiting on this device that saving the
 * values as they stand would remove, because they were made on something the edit no longer makes (an expense, a tyre log line, a
 * break-up's tyres). Worked out again whenever `values` say something else or the changes waiting move; in a status region, so it is said
 * when it comes and goes. Save asks before it removes them (`EditChange`).
 */
export function LeftBehindNote({ edit, values }: { edit?: Pick<ChangeEdit<unknown>, 'leftBehind'>; values: () => Promise<Record<string, unknown> | null> }) {
  const [labels, setLabels] = useState<string[]>([])
  const pending = usePendingCount()
  const labelsOf = useLeftBehindLabels()
  // The values as they stand, compared by their words: the dialog hands a new function on every render.
  const [key, setKey] = useState('')
  useEffect(() => {
    let live = true
    void values()
      .then((v) => live && setKey(JSON.stringify(v)))
      .catch(() => live && setKey(''))
    return () => {
      live = false
    }
  })
  useEffect(() => {
    if (!edit?.leftBehind) return
    let live = true
    const values = key ? (JSON.parse(key) as Record<string, unknown> | null) : null
    const behind = values ? edit.leftBehind(values) : []
    void labelsOf(behind)
      .then((l) => live && setLabels(l))
      .catch(() => live && setLabels([]))
    return () => {
      live = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the values by their words, the outbox by its count
  }, [key, pending, edit?.leftBehind])
  if (!edit?.leftBehind) return null
  return (
    <div role="status">
      {labels.length > 0 && (
        // Its own region says it (politely): no alert of its own inside it.
        <Alert severity="warning" role="none">
          <LeftBehindText labels={labels} />
        </Alert>
      )}
    </div>
  )
}
