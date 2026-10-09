import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { offlineReady, useOfflineReady } from '../pwa/offlineReady.ts'
import { useToast } from '../toast/toastContext.ts'

/**
 * Says once, when the app has just been stored on this device for the first time (a first visit), that it can now be used offline. Until
 * then a page not opened yet needs the server; the Offline data panel says which it is at any time.
 */
export function OfflineReadyNotice() {
  const { t } = useTranslation()
  const { toast } = useToast()
  const state = useOfflineReady()
  useEffect(() => {
    if (state === 'ready' && offlineReady.takeNews()) toast({ message: t('app.offlineReady'), severity: 'success' })
  }, [state, toast, t])
  return null
}
