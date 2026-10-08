import { useApolloClient } from '@apollo/client/react'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import { useTranslation } from 'react-i18next'
import { useErrorText } from '../i18n/errors.ts'
import type { ChangeEntity } from '../offline/changes.ts'
import { keepEntry } from '../offline/submitChange.ts'
import { useToast } from '../toast/toastContext.ts'

/**
 * Keep, in the actions of a row waiting on this device to be trashed (or deleted): takes that back. In place of Edit and Delete, since
 * changing what is about to go would change nothing. `name` is what the row is called in the button's name.
 */
export function KeepButton({ entity, id, name }: { entity: ChangeEntity; id: string; name: string }) {
  const { t } = useTranslation()
  const client = useApolloClient()
  const { toast } = useToast()
  const errorText = useErrorText()
  const keep = async () => {
    try {
      await keepEntry(client, entity, id)
      toast(t('sync.kept'))
    } catch (error) {
      toast({ message: errorText(error), severity: 'error' }) // the row stays marked: say why
    }
  }
  return (
    <Stack direction="row" sx={{ justifyContent: 'flex-end' }}>
      <Button variant="soft" size="large" aria-label={t('sync.keepAria', { name })} onClick={() => void keep()}>
        {t('sync.keep')}
      </Button>
    </Stack>
  )
}
