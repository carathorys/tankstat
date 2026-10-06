import { useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'
import { HealthDocument } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { StatusBadge } from '../StatusBadge.tsx'

/** The API's health and version at the foot of every screen, clear of the rounded corners and the home indicator of an installed app. */
export function HealthFooter() {
  const { t } = useTranslation()
  const { data, error } = useQuery(HealthDocument)

  return (
    <Box
      component="footer"
      sx={(theme) => ({
        borderTop: `1px solid ${theme.vars.palette.divider}`,
        pt: 1,
        pl: 'max(16px, env(safe-area-inset-left, 0px))',
        pr: 'max(16px, env(safe-area-inset-right, 0px))',
        pb: 'calc(8px + env(safe-area-inset-bottom, 0px))',
      })}
    >
      {error && <ErrorMessage>{t('status.unreachable', { message: error.message })}</ErrorMessage>}
      {data && (
        <Typography variant="caption" sx={{ color: 'text.secondary' }}>
          {t('status.api')} <StatusBadge status={data.health.status} /> (v{data.health.version})
        </Typography>
      )}
    </Box>
  )
}
