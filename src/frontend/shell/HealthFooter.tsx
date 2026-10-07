import { useApolloClient, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { CloudOff } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { HealthDocument } from '../gql/generated.ts'
import { probeServer } from '../offline/runtime.ts'
import { useConnectivity } from '../offline/useConnectivity.ts'
import { StatusBadge } from '../StatusBadge.tsx'

/**
 * The API's health and version at the foot of every screen, clear of the rounded corners and the home indicator of an installed app.
 * While the server is out of reach it says so (offline, or the server is down) with a way to try again at once.
 */
export function HealthFooter() {
  const { t } = useTranslation()
  const client = useApolloClient()
  const { data, error } = useQuery(HealthDocument)
  const { reachable } = useConnectivity()

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
      {!reachable || error ? (
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <CloudOff size={14} aria-hidden />
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {navigator.onLine ? t('status.unreachable') : t('status.offline')}
          </Typography>
          <Button size="small" variant="ghost" onClick={() => void probeServer(client)}>
            {t('status.retry')}
          </Button>
        </Stack>
      ) : (
        data && (
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            {t('status.api')} <StatusBadge status={data.health.status} /> (v{data.health.version})
          </Typography>
        )
      )}
    </Box>
  )
}
