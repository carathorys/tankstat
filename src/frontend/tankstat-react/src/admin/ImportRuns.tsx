import { useMutation } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { ConfirmDialog } from '../components/ConfirmDialog.tsx'
import { CancelImportRunDocument, type ImportRunFieldsFragment } from '../gql/generated.ts'
import { useImportRuns } from './useImportRuns.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { useToast } from '../toast/toastContext.ts'

/** One line per import that goes or waits, each with *Cancel it* behind a confirmation. */
export function ImportRunLines({ runs, onCancelled }: { runs: readonly ImportRunFieldsFragment[]; onCancelled: () => void }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const { toast } = useToast()
  const [cancel, cancelling] = useMutation(CancelImportRunDocument)
  return (
    <Stack component="ul" sx={{ gap: 1, m: 0, p: 0, listStyle: 'none' }}>
      {runs.map((run) => {
        const name = run.startedByName ?? ''
        const cars = run.cars.length
        const text =
          run.state === 'RUNNING'
            ? t('admin.imports.running', { name, time: dateTime(run.startedAt ?? run.queuedAt), cars, done: run.progress.done, total: run.progress.total })
            : t('admin.imports.waiting', { name, time: dateTime(run.queuedAt), cars })
        return (
          <Stack key={run.id} component="li" direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography sx={{ flex: '1 1 16rem', minWidth: 0 }}>{text}</Typography>
            <ConfirmDialog
              trigger={
                <Button variant="soft" color="error" disabled={cancelling.loading} aria-label={`${t('admin.imports.cancel')}: ${name}`}>
                  {t('admin.imports.cancel')}
                </Button>
              }
              title={t('import.run.cancelTitle')}
              description={t('import.run.cancelText')}
              confirmLabel={t('import.run.cancel')}
              onConfirm={() =>
                void cancel({ variables: { id: run.id } })
                  .then(() => {
                    toast(t('admin.imports.cancelled'))
                    onCancelled()
                  })
                  .catch(() => undefined)
              }
            />
          </Stack>
        )
      })}
      {cancelling.error && (
        <li>
          <ErrorMessage error={cancelling.error} />
        </li>
      )}
    </Stack>
  )
}

/** The Backups page's section: shown only while an import runs or waits. */
export function ImportRunsSection() {
  const { t } = useTranslation()
  const heading = useId()
  const { runs, refetch } = useImportRuns()
  if (runs.length === 0) return null
  return (
    <section aria-labelledby={heading}>
      <Typography id={heading} component="h2" variant="h5" sx={{ mb: 0.5 }}>
        {t('admin.imports.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 1 }}>
        {t('admin.imports.hint')}
      </Typography>
      <ImportRunLines runs={runs} onCancelled={refetch} />
    </section>
  )
}
