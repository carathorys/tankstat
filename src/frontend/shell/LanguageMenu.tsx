import { DropdownMenu, IconButton } from '@radix-ui/themes'
import { Languages } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { LANGUAGES } from '../i18n/index.ts'

/** Available on every screen (also before sign-in); the choice is remembered in the browser. */
export function LanguageMenu() {
  const { t, i18n } = useTranslation()
  const current = i18n.resolvedLanguage ?? 'en'

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger>
        <IconButton variant="soft" color="gray" highContrast aria-label={t('language.label')}>
          <Languages size={18} />
        </IconButton>
      </DropdownMenu.Trigger>
      <DropdownMenu.Content align="end">
        <DropdownMenu.RadioGroup value={current} onValueChange={(code) => void i18n.changeLanguage(code)}>
          {LANGUAGES.map((l) => (
            <DropdownMenu.RadioItem key={l.code} value={l.code} lang={l.code}>
              {l.name}
            </DropdownMenu.RadioItem>
          ))}
        </DropdownMenu.RadioGroup>
      </DropdownMenu.Content>
    </DropdownMenu.Root>
  )
}
