import { Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'

/** Next to a Save button that waits for photos to finish uploading: says why it waits. */
export function SaveWait({ waiting }: { waiting: boolean }) {
  const { t } = useTranslation()
  if (!waiting) return null
  return (
    <Text size="1" color="gray" className="tk-appear" style={{ marginRight: 'auto', alignSelf: 'center' }}>
      {t('photos.waitUpload')}
    </Text>
  )
}
