import { useColorScheme } from '@mui/material/styles'
import { Moon, Sun, SunMoon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useUiSettings } from '../settings/uiSettingsContext.ts'
import { COLOR_MODES, type ColorModeChoice } from '../theme/colorMode.ts'
import { RadioMenu } from './RadioMenu.tsx'

const ICONS = { light: Sun, dark: Moon, system: SunMoon } as const

/**
 * Light, dark, or as the device is set (system), next to the language. Available on every screen; remembered in the browser and, once
 * signed in, with the account. The button shows the current choice.
 */
export function ColorModeMenu() {
  const { t } = useTranslation()
  const { mode, setMode } = useColorScheme()
  const settings = useUiSettings()
  const current: ColorModeChoice = mode ?? 'dark'
  const Icon = ICONS[current]
  return (
    <RadioMenu
      label={t('colorMode.label')}
      icon={<Icon size={18} aria-hidden />}
      value={current}
      options={COLOR_MODES.map((m) => ({ value: m, label: t(`colorMode.${m}`) }))}
      onChange={(next) => {
        setMode(next)
        settings?.setColorMode(next)
      }}
    />
  )
}
