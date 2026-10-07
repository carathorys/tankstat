import Alert from '@mui/material/Alert'
import Stack from '@mui/material/Stack'
import { Info, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { SessionQuery } from './gql/generated.ts'

/** Shows every notice the server sent, translated by its code (the server text is only a fallback). */
export function NoticeBanner({ notices }: { notices: SessionQuery['notices'] }) {
  const { t, i18n } = useTranslation()
  if (notices.length === 0) return null

  return (
    <Stack component="aside" aria-label={t('notices.label')} sx={{ gap: 1, mb: 2 }}>
      {notices.map((n) => {
        const warning = n.severity === 'WARNING'
        const path = `notices.${n.code}`
        return (
          <Alert
            key={n.code}
            role="note"
            data-severity={n.severity}
            severity={warning ? 'warning' : 'info'}
            icon={warning ? <TriangleAlert size={16} aria-hidden /> : <Info size={16} aria-hidden />}
            sx={{ p: 2, gap: 1.5 }} // a page's notice: roomier than a message in a dialog
          >
            {warning && <strong>{t('notices.warning')} </strong>}
            {i18n.exists(path) ? t(path as never) : n.message}
          </Alert>
        )
      })}
    </Stack>
  )
}
