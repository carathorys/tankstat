import { Badge, Box, Flex, IconButton, Text } from '@radix-ui/themes'
import { Menu } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { UserAvatar } from '../components/UserAvatar.tsx'
import type { SessionQuery } from '../gql/generated.ts'
import { LanguageMenu } from './LanguageMenu.tsx'

/** Shown on every screen; the menu button appears once the visitor may use the app (signed in, or auth is off). */
export function TopBar({
  user,
  showMenu,
  navOpen,
  onToggleNav,
}: {
  user: SessionQuery['session']['user']
  showMenu: boolean
  navOpen: boolean
  onToggleNav: () => void
}) {
  const { t } = useTranslation()

  return (
    <Flex asChild align="center" gap="3" px="4" py="2" className="glass app-topbar">
      <header>
        {showMenu && (
          <IconButton
            variant="soft"
            color="gray"
            size="3"
            aria-label={navOpen ? t('nav.hide') : t('nav.show')}
            aria-expanded={navOpen}
            aria-controls="app-nav"
            onClick={onToggleNav}
          >
            <Menu size={20} aria-hidden />
          </IconButton>
        )}
        <Text asChild size="5" weight="bold">
          <Link to="/" style={{ color: 'inherit', textDecoration: 'none' }}>
            {t('app.name')}
          </Link>
        </Text>
        <Box flexGrow="1" />
        {user && (
          <Flex align="center" gap="2" aria-label={t('nav.signedInAs', { name: user.displayName })} role="group">
            <UserAvatar user={user} />
            <Text size="2" className="topbar-name">
              {user.displayName}
            </Text>
            {user.isAdmin && (
              <Badge color="gray" highContrast>
                {t('role.adminBadge')}
              </Badge>
            )}
          </Flex>
        )}
        <LanguageMenu />
      </header>
    </Flex>
  )
}
