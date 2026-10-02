import { Button, Callout, Dialog, Flex } from '@radix-ui/themes'
import { AlertTriangle } from 'lucide-react'
import { useTranslation } from 'react-i18next'

/**
 * Shown in place of the form when a new log was saved but the server could not attach some of its photos. The log exists, so it must
 * not be saved again; the photos can be added once more by editing it.
 */
export function PhotosLeftOut({ count }: { count: number }) {
  const { t } = useTranslation()
  return (
    <Flex direction="column" gap="3">
      <Callout.Root color="amber" role="alert">
        <Callout.Icon>
          <AlertTriangle size={16} aria-hidden />
        </Callout.Icon>
        <Callout.Text>{t('photos.leftOut', { count })}</Callout.Text>
      </Callout.Root>
      <Flex justify="end">
        <Dialog.Close>
          <Button type="button">{t('photos.done')}</Button>
        </Dialog.Close>
      </Flex>
    </Flex>
  )
}
