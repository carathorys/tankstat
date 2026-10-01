import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'

/** Sets the browser tab / screen reader page title to "<page> – Tankstat". */
export function usePageTitle(title: string | undefined) {
  const { t } = useTranslation()
  useEffect(() => {
    document.title = title ? `${title} – ${t('app.name')}` : t('app.name')
  }, [title, t])
}
