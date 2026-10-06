import { Button, Dialog, Flex, Text } from '@radix-ui/themes'
import { Download } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useInstallPrompt } from '../pwa/useInstallPrompt.ts'
import { navButtonStyle } from './navStyles.ts'

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
        color="gray"
        size="3"
        style={navButtonStyle}
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
  if (!showIosHint) return null
  // The drawer stays open here: the dialog lives inside it and would go with it.
  return (
    <Dialog.Root>
      <Dialog.Trigger>
        <Button variant="ghost" color="gray" size="3" style={navButtonStyle}>
          <Download size={18} aria-hidden />
          {t('nav.install')}
        </Button>
      </Dialog.Trigger>
      <Dialog.Content maxWidth="420px">
        <Dialog.Title>{t('install.title')}</Dialog.Title>
        <Dialog.Description size="2" mb="3">
          {t('install.iosIntro')}
        </Dialog.Description>
        <Flex asChild direction="column" gap="2">
          <ol style={{ margin: 0, paddingLeft: 'var(--space-5)' }}>
            <li>
              <Text size="2">{t('install.iosShare')}</Text>
            </li>
            <li>
              <Text size="2">{t('install.iosAdd')}</Text>
            </li>
            <li>
              <Text size="2">{t('install.iosConfirm')}</Text>
            </li>
          </ol>
        </Flex>
        <Flex justify="end" mt="4">
          <Dialog.Close>
            <Button variant="soft" color="gray" size="3" style={{ minHeight: 44 }}>
              {t('common.close')}
            </Button>
          </Dialog.Close>
        </Flex>
      </Dialog.Content>
    </Dialog.Root>
  )
}
