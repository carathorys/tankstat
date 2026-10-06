import { Flex, Spinner, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'

/** "Loading…" with a spinner that shows only once the wait is noticeable (no flash on quick loads); a status for screen readers. */
export function Loading({ label }: { label?: string }) {
  const { t } = useTranslation()
  return (
    <Flex align="center" gap="2" role="status" my="2">
      <Spinner size="2" className="tk-delayed" />
      <Text size="2" color="gray">
        {label ?? t('app.loading')}
      </Text>
    </Flex>
  )
}
