import { useColorScheme } from '@mui/material/styles'
import { Moon, Sun, SunMoon } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useUiSettings } from '../settings/uiSettingsContext.ts'
import { COLOR_MODES, type ColorModeChoice } from '../theme/colorMode.ts'
import { SURFACES, type SurfaceChoice } from '../theme/surface.ts'
import { setSurface, useSurface } from '../theme/surfaceStore.ts'
import { RadioMenu } from './RadioMenu.tsx'

const ICONS = { light: Sun, dark: Moon, system: SunMoon } as const

/**
 * How the app looks, next to the language: the colour mode (light, dark, or as the device is set) and the surfaces (glossy, transparent
 * or opaque). Available on every screen; remembered in the browser and, once signed in, with the account. The button shows the colour
 * mode in effect.
 */
export function AppearanceMenu() {
  const { t } = useTranslation()
  const { mode, setMode } = useColorScheme()
  const surface = useSurface()
  const settings = useUiSettings()
  const current: ColorModeChoice = mode ?? 'dark'
  const Icon = ICONS[current]
  return (
    <RadioMenu
      label={t('appearance.label')}
      icon={<Icon size={18} aria-hidden />}
      groups={[
        {
          label: t('colorMode.label'),
          value: current,
          options: COLOR_MODES.map((m) => ({ value: m, label: t(`colorMode.${m}`) })),
          onChange: (next) => {
            setMode(next as ColorModeChoice)
            settings?.setColorMode(next as ColorModeChoice)
          },
        },
        {
          label: t('surface.label'),
          value: surface,
          options: SURFACES.map((s) => ({ value: s, label: t(`surface.${s}`) })),
          onChange: (next) => {
            setSurface(next as SurfaceChoice)
            settings?.setSurface(next as SurfaceChoice)
          },
        },
      ]}
    />
  )
}
