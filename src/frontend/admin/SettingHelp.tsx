import IconButton from '@mui/material/IconButton'
import Stack from '@mui/material/Stack'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { CircleHelp } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { envName, helpKey } from './settingsText.ts'

/**
 * What a setting does and the environment variable that sets it, behind a question mark next to it: shown on hover, on keyboard focus and on
 * a tap (a tooltip alone would leave touch and keyboard out), and read out as the button's description, so a screen reader hears it too.
 */
export function SettingHelp({ settingKey, label }: { settingKey: string; label: string }) {
  const { t, i18n } = useTranslation()
  const help = i18n.exists(helpKey(settingKey)) ? String(t(helpKey(settingKey) as never)) : null
  return (
    <Tooltip
      describeChild
      enterTouchDelay={0}
      leaveTouchDelay={8000}
      title={
        <Stack sx={{ gap: 0.5, py: 0.25 }}>
          {help && <span>{help}</span>}
          <Typography component="span" variant="caption" sx={{ fontFamily: 'monospace', opacity: 0.85 }}>
            {t('systemSettings.variable', { variable: envName(settingKey) })}
          </Typography>
        </Stack>
      }
    >
      <IconButton size="small" aria-label={t('systemSettings.about', { setting: label })} sx={{ color: 'text.secondary', flexShrink: 0 }}>
        <CircleHelp size={16} aria-hidden />
      </IconButton>
    </Tooltip>
  )
}
