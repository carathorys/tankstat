import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { Hourglass, ScanText } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { FieldMessage } from '../forms/Field.tsx'

/**
 * Under a field that a photo filled in: "Read from the photo; check it". Where the user typed something else: "The photo shows …" with a
 * Use button. An empty field that may wait for a photo still being read says so ("Leave it empty …"). Goes into a `Field`'s `extra`
 * slot, so the text is linked to the control like its hint.
 */
export function ReadNote({
  filled,
  offered,
  waiting,
  field,
  onUse,
}: {
  filled: boolean
  offered?: string
  /** The field is empty and may stay so: a photo that is still being read fills it in after saving. */
  waiting?: boolean
  field: string
  onUse?: () => void
}) {
  const { t } = useTranslation()
  const icon = { verticalAlign: '-2px', marginRight: 4 }
  if (offered !== undefined)
    return (
      <Stack direction="row" className="tk-appear" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <FieldMessage sx={{ color: 'text.primary' }}>
          <ScanText size={12} aria-hidden style={icon} />
          {t('reading.differs', { value: offered })}
        </FieldMessage>
        <Button type="button" variant="soft" sx={{ minHeight: 44 }} aria-label={t('reading.useAria', { value: offered, field })} onClick={onUse}>
          {t('reading.use')}
        </Button>
      </Stack>
    )
  if (filled)
    return (
      <FieldMessage className="tk-appear" sx={{ color: 'info.softText' }}>
        <ScanText size={12} aria-hidden style={icon} />
        {t('reading.filled')}
      </FieldMessage>
    )
  if (waiting)
    return (
      <FieldMessage className="tk-appear">
        <Hourglass className="tk-pulse" size={12} aria-hidden style={icon} />
        {t('reading.waiting')}
      </FieldMessage>
    )
  return null
}
