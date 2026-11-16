import { useTranslation } from 'react-i18next'
import { DismissTyreReminderDocument, RestoreTyreReminderDocument } from '../gql/generated.ts'
import { useTyreMutation, useTyreToasts } from './tyres.ts'

/**
 * *Not this season* (#292, the owner's decisions 73 and 77): the set's swap reminder is off for that season, for everyone, with a toast
 * whose Undo takes it back (a dismissal still waiting on this device is simply dropped). `after` runs once each has settled (the home
 * card asks for its card again).
 */
export function useReminderDismiss(vehicleId: string, after?: () => unknown) {
  const { t } = useTranslation()
  const run = useTyreMutation(vehicleId)
  const notify = useTyreToasts()
  return async (setId: string, season: string) => {
    const saved = await run(DismissTyreReminderDocument, { input: { id: setId, season } })
    notify.undoable(saved, t('tyres.reminder.dismissed'), async () => {
      await run(RestoreTyreReminderDocument, { id: setId })
      await after?.()
    })
    await after?.()
  }
}
