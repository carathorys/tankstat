import { Languages } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LANGUAGES } from '../i18n/index.ts'
import { useUiSettings } from '../settings/uiSettingsContext.ts'
import { RadioMenu } from './RadioMenu.tsx'

/** Available on every screen (also before sign-in); the choice is remembered in the browser and, once signed in, with the account. */
export function LanguageMenu() {
  const { t, i18n } = useTranslation()
  const settings = useUiSettings()
  return (
    <RadioMenu
      label={t('language.label')}
      icon={<Languages size={18} aria-hidden />}
      value={i18n.resolvedLanguage ?? 'en'}
      options={LANGUAGES.map((l) => ({ value: l.code, label: l.name, lang: l.code }))}
      onChange={(code) => {
        void i18n.changeLanguage(code)
        settings?.setLanguage(code)
      }}
    />
  )
}
