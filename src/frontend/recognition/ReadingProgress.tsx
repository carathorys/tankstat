import { Flex, Spinner, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'

/**
 * While a photo is being read: says so where the user looks before saving, and whether they can save right away (what they leave empty is
 * then filled in from the photo, and the log is marked for review). Goes inside the dialog's reading status region.
 */
export function ReadingProgress({ active, canSave }: { active: boolean; canSave: boolean }) {
  const { t } = useTranslation()
  if (!active) return null
  return (
    <Flex align="center" gap="2" className="tk-appear">
      <Spinner size="1" />
      <Text size="2">
        {t('reading.progress')}
        {canSave && ` ${t('reading.saveNow')}`}
      </Text>
    </Flex>
  )
}
