import * as RadixForm from '@radix-ui/react-form'
import { Button, Flex, Text } from '@radix-ui/themes'
import { ScanText } from 'lucide-react'
import { useTranslation } from 'react-i18next'

/**
 * Under a field that a photo filled in: "Read from the photo; check it". Where the user typed something else: "The photo shows …" with a
 * Use button. Goes into a `Field`'s `extra` slot, so the text is linked to the control like its hint.
 */
export function ReadNote({ filled, offered, field, onUse }: { filled: boolean; offered?: string; field: string; onUse: () => void }) {
  const { t } = useTranslation()
  if (offered !== undefined)
    return (
      <Flex align="center" gap="2" wrap="wrap">
        <RadixForm.Message forceMatch asChild>
          <Text size="1">
            <ScanText size={12} aria-hidden /> {t('reading.differs', { value: offered })}
          </Text>
        </RadixForm.Message>
        <Button type="button" size="2" variant="soft" style={{ minHeight: 44 }} aria-label={t('reading.useAria', { value: offered, field })} onClick={onUse}>
          {t('reading.use')}
        </Button>
      </Flex>
    )
  if (filled)
    return (
      <RadixForm.Message forceMatch asChild>
        <Text size="1" color="blue">
          <ScanText size={12} aria-hidden /> {t('reading.filled')}
        </Text>
      </RadixForm.Message>
    )
  return null
}
