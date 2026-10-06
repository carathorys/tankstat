import { useQuery } from '@apollo/client/react'
import { Box, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { HealthDocument } from '../gql/generated.ts'
import { ErrorMessage } from '../messages.tsx'
import { StatusBadge } from '../StatusBadge.tsx'

export function HealthFooter() {
  const { t } = useTranslation()
  const { data, error } = useQuery(HealthDocument)

  return (
    <Box asChild px="4" py="2" className="app-footer" style={{ borderTop: '1px solid var(--gray-6)' }}>
      <footer>
        {error && <ErrorMessage>{t('status.unreachable', { message: error.message })}</ErrorMessage>}
        {data && (
          <Text size="1" color="gray">
            {t('status.api')} <StatusBadge status={data.health.status} /> (v{data.health.version})
          </Text>
        )}
      </footer>
    </Box>
  )
}
