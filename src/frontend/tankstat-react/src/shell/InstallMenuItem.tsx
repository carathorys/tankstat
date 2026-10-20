import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Download } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'
import { useInstallPrompt } from '../pwa/useInstallPrompt.ts'
import { navItemSx } from './navStyles.ts'

/**
 * "Install app" in the navigation menu. With a browser that offers to install (Chromium) it shows the browser's own dialog; on an iPhone
 * or iPad it explains Safari's Share → Add to Home Screen; nothing anywhere else, and nothing once the app runs installed. `onNavigate`
 * lets the phone drawer close when the browser's dialog takes over.
 */
export function InstallMenuItem({ onNavigate }: { onNavigate?: () => void }) {
  const { t } = useTranslation()
  const { canInstall, install, showIosHint } = useInstallPrompt()

  if (canInstall) {
    return (
      <Button
        variant="ghost"
        color="neutral"
        sx={navItemSx}
        onClick={() => {
          onNavigate?.()
          void install()
        }}
      >
        <Download size={18} aria-hidden />
        {t('nav.install')}
      </Button>
    )
  }
  return showIosHint ? <IosInstallHint /> : null
}

/** Safari's way, step by step. The drawer stays open meanwhile: this dialog opened from it. */
function IosInstallHint() {
  const { t } = useTranslation()
  const [open, setOpen] = useDialogState()
  return (
    <>
      <DialogTrigger
        trigger={
          <Button variant="ghost" color="neutral" sx={navItemSx}>
            <Download size={18} aria-hidden />
            {t('nav.install')}
          </Button>
        }
        open={open}
        onOpen={() => setOpen(true)}
      />
      <DialogFrame open={open} onClose={() => setOpen(false)} maxWidth={420} title={t('install.title')} description={t('install.iosIntro')}>
        <Stack component="ol" sx={{ m: 0, mb: 2, pl: 3, gap: 1 }}>
          {[t('install.iosShare'), t('install.iosAdd'), t('install.iosConfirm')].map((step) => (
            <li key={step}>
              <Typography variant="body2">{step}</Typography>
            </li>
          ))}
        </Stack>
        <DialogButtons>
          <DialogCancel label={t('common.close')} />
        </DialogButtons>
      </DialogFrame>
    </>
  )
}
