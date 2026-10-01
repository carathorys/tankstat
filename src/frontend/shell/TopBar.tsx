import { Badge, Box, Flex, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { AuthMode, SessionQuery } from '../gql/generated.ts'
import { HamburgerMenu } from './HamburgerMenu.tsx'
import { LanguageMenu } from './LanguageMenu.tsx'

/** Shown on every screen; the menu appears once the visitor may use the app (signed in, or auth is off). */
export function TopBar({
  mode,
  user,
  showMenu,
}: {
  mode: AuthMode
  user: SessionQuery['session']['user']
  showMenu: boolean
}) {
  const { t } = useTranslation()

  return (
    <Flex
      asChild
      align="center"
      gap="3"
      px="4"
      py="2"
      position="sticky"
      top="0"
      style={{ background: 'var(--accent-9)', color: 'var(--accent-contrast)', zIndex: 5 }}
    >
      <header>
        {showMenu && <HamburgerMenu mode={mode} user={user} />}
        <Text asChild size="5" weight="bold">
          <Link to="/" style={{ color: 'inherit', textDecoration: 'none' }}>
            {t('app.name')}
          </Link>
        </Text>
        <Box flexGrow="1" />
        {user && (
          <Flex align="center" gap="2" display={{ initial: 'none', xs: 'flex' }}>
            <Text size="2">{user.displayName}</Text>
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
